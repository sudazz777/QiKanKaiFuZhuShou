using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class SteamRuntime {
 const string ActiveKey=@"Software\Valve\Steam\ActiveProcess";
 const uint QueryLimitedInformation=0x1000,TokenQuery=0x0008,StillActive=259;

 // SteamAPI.IsSteamRunning reads this record. A successful SteamAPI.Init does not
 // guarantee that Steam has populated it after a restart.
 internal static string EnsureProcessRecord(string steamExe,string backupRoot) {
  try {
   string expected=FullPath(steamExe);
   if(expected==""||!string.Equals(Path.GetFileName(expected),"Steam.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(expected))
    throw new InvalidOperationException("未找到已适配的 Steam.exe，未修改 Steam 进程记录");
   if(string.IsNullOrWhiteSpace(backupRoot))throw new InvalidOperationException("缺少 Steam 进程记录备份目录，未修改记录");
   Record before;
   using(var key=Registry.CurrentUser.OpenSubKey(ActiveKey,false)) {
    before=ReadPid(key);
    // Preserve every live PID, including an ambiguous or reused process ID.
    if(before.Pid!=0&&ProcessExists(before.Pid))return null;
    ReadMetadata(key,expected,before);
   }

   int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
   string sid;using(var identity=WindowsIdentity.GetCurrent()) {
    if(identity.User==null)throw new InvalidOperationException("无法确认当前 Windows 用户，未修改 Steam 进程记录");
    sid=identity.User.Value;
   }
   var candidates=new List<SteamProcess>();
   try {
    foreach(var process in Process.GetProcessesByName("steam")) {
     using(process) {
      SteamProcess candidate=OpenSteam(process.Id,expected,session,sid);
      if(candidate!=null)candidates.Add(candidate);
     }
    }
    if(candidates.Count!=1)throw new InvalidOperationException(candidates.Count==0?
     "未找到当前用户、当前会话中与已适配路径一致的 Steam 进程，未修改记录":
     "检测到多个符合条件的 Steam 进程，未修改记录；请保留一个 Steam 客户端");
    SteamProcess selected=candidates[0];
    if(!IsAlive(selected.Handle))throw new InvalidOperationException("Steam 已退出，未修改进程记录");
    string backupDir=Path.Combine(Path.GetFullPath(backupRoot),"steam-process-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(backupDir);
    var backup=new Dictionary<string,object> {
     {"registryKey",@"HKEY_CURRENT_USER\"+ActiveKey},{"valueName","pid"},
     {"oldValue",before.Pid},{"oldValueKind",before.Kind.ToString()},
     {"newValue",selected.Pid},{"savedAtUtc",DateTime.UtcNow.ToString("o")},
     {"steamExecutable",expected},{"sessionId",session},{"userSid",sid},
     {"steamClientDll",before.ClientDll},{"steamClientDll64",before.ClientDll64}
    };
    File.WriteAllText(Path.Combine(backupDir,"steam-activeprocess-pid.json"),new JavaScriptSerializer().Serialize(backup),new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(backupDir,"restore-pid.reg"),"Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\"+ActiveKey+"]\r\n\"pid\"=dword:"+before.Pid.ToString("x8")+"\r\n",Encoding.Unicode);

    using(var key=Registry.CurrentUser.OpenSubKey(ActiveKey,true)) {
     Record current=ReadRecord(key,expected);
     if(!SameRecord(before,current))throw new InvalidOperationException("Steam 进程记录在检查期间已变化，未覆盖新记录");
     if(current.Pid!=0&&ProcessExists(current.Pid))return null;
     if(!IsAlive(selected.Handle))throw new InvalidOperationException("Steam 已退出，未修改进程记录");
     key.SetValue("pid",selected.Pid,RegistryValueKind.DWord);
     key.Flush();
     object written=key.GetValue("pid",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
     if(!(written is int)||unchecked((uint)(int)written)!=(uint)selected.Pid)
      throw new InvalidOperationException("Steam 进程记录写入后未能核验，请查看备份目录："+backupDir);
    }
    return "已修复 Steam 的失效进程记录。备份："+backupDir;
   } finally {foreach(var candidate in candidates)candidate.Dispose();}
  } catch(Exception error) {
   throw new InvalidOperationException("Steam 进程检测失败："+error.Message,error);
  }
 }

 sealed class Record {
  internal uint Pid;internal RegistryValueKind Kind;
  internal string ClientDll,ClientDll64;
 }
 static Record ReadPid(RegistryKey key) {
  if(key==null)throw new InvalidOperationException("Steam 尚未建立进程记录；请先打开 Steam 并登录");
  object value=key.GetValue("pid",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
  if(!(value is int)||key.GetValueKind("pid")!=RegistryValueKind.DWord)
   throw new InvalidOperationException("Steam 的 pid 记录不存在或类型不符，未修改记录");
  var record=new Record {Pid=unchecked((uint)(int)value),Kind=RegistryValueKind.DWord};
  if(record.Pid>int.MaxValue)throw new InvalidOperationException("Steam 的 pid 记录超出可核验范围，未修改记录");
  return record;
 }
 static Record ReadRecord(RegistryKey key,string expected) {var record=ReadPid(key);ReadMetadata(key,expected,record);return record;}
 static void ReadMetadata(RegistryKey key,string expected,Record record) {
  record.ClientDll=ReadDll(key,"SteamClientDll",expected,"steamclient.dll");
  record.ClientDll64=ReadDll(key,"SteamClientDll64",expected,"steamclient64.dll");
  if(record.ClientDll==""&&record.ClientDll64=="")throw new InvalidOperationException("Steam 进程记录缺少客户端路径，未修改记录");
 }
 static string ReadDll(RegistryKey key,string name,string expected,string dllName) {
  object value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
  if(value==null)return "";
  var kind=key.GetValueKind(name);
  if(!(value is string)||(kind!=RegistryValueKind.String&&kind!=RegistryValueKind.ExpandString))
   throw new InvalidOperationException("Steam 客户端路径记录类型异常，未修改记录");
  string text=(string)value;
  string full=FullPath(kind==RegistryValueKind.ExpandString?Environment.ExpandEnvironmentVariables(text):text);
  string required=Path.Combine(Path.GetDirectoryName(expected),dllName);
  if(!string.Equals(full,required,StringComparison.OrdinalIgnoreCase)||!File.Exists(full))
   throw new InvalidOperationException("Steam 进程记录的客户端路径与已适配 Steam 不一致，未修改记录");
  return text;
 }
 static bool SameRecord(Record left,Record right) {
  return left.Pid==right.Pid&&left.Kind==right.Kind&&
   string.Equals(left.ClientDll,right.ClientDll,StringComparison.Ordinal)&&
   string.Equals(left.ClientDll64,right.ClientDll64,StringComparison.Ordinal);
 }
 static string FullPath(string path) {
  if(string.IsNullOrWhiteSpace(path))return "";
  try {return Path.GetFullPath(path.Trim().Trim('"').Replace('/',Path.DirectorySeparatorChar));}catch{return "";}
 }
 static bool ProcessExists(uint pid) {
  try {using(var process=Process.GetProcessById((int)pid))return !process.HasExited;}
  catch(ArgumentException){return false;}
  // Access errors must not be interpreted as a stale record.
 }
 sealed class SteamProcess : IDisposable {
  internal int Pid;internal IntPtr Handle;
  public void Dispose(){if(Handle!=IntPtr.Zero){CloseHandle(Handle);Handle=IntPtr.Zero;}}
 }
 static SteamProcess OpenSteam(int pid,string expected,int session,string sid) {
  uint processSession;if(!ProcessIdToSessionId((uint)pid,out processSession))
   throw new InvalidOperationException("无法核验 Steam 会话，未修改记录");
  if(processSession!=(uint)session)return null;
  IntPtr handle=OpenProcess(QueryLimitedInformation,false,pid);
  if(handle==IntPtr.Zero)throw new InvalidOperationException("无法核验 Steam 进程权限，未修改记录");
  bool keep=false;
  try {
   // Recheck after opening the handle, in case the PID changed during enumeration.
   if(!ProcessIdToSessionId((uint)pid,out processSession))throw new InvalidOperationException("无法核验 Steam 会话，未修改记录");
   if(processSession!=(uint)session)return null;
   var image=new StringBuilder(32768);uint length=(uint)image.Capacity;
   if(!QueryFullProcessImageName(handle,0,image,ref length))throw new InvalidOperationException("无法核验 Steam 程序路径，未修改记录");
   if(!string.Equals(FullPath(image.ToString()),expected,StringComparison.OrdinalIgnoreCase))return null;
   IntPtr token;
   if(!OpenProcessToken(handle,TokenQuery,out token))throw new InvalidOperationException("无法核验 Steam 所属用户，未修改记录");
   try {
    using(var identity=new WindowsIdentity(token)) {
     if(identity.User==null||!string.Equals(identity.User.Value,sid,StringComparison.Ordinal))return null;
    }
   } finally {CloseHandle(token);}
   if(!IsAlive(handle))return null;
   keep=true;return new SteamProcess {Pid=pid,Handle=handle};
  } finally {if(!keep)CloseHandle(handle);}
 }
 static bool IsAlive(IntPtr handle) {
  uint code;if(!GetExitCodeProcess(handle,out code))throw new InvalidOperationException("无法核验 Steam 运行状态，未修改记录");
  return code==StillActive;
 }
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inheritHandle,int processId);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr process,out uint exitCode);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ProcessIdToSessionId(uint processId,out uint sessionId);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder exeName,ref uint size);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
}
