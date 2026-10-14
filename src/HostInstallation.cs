using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class HostInstallation {
 public string SteamExe="",GameDir="",ServerDir="";
 internal string DocumentsOverride;
 public static HostInstallation Current=new HostInstallation();
 static readonly Encoding Utf8=new UTF8Encoding(false);
 public string GameUserDir {get {string docs=DocumentsOverride??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);if(string.IsNullOrEmpty(docs))docs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents");return Path.Combine(docs,"Mount and Blade II Bannerlord");}}
 public string DataDir {get {return Path.Combine(GameUserDir,"CoopData","DedicatedServer");}}
 public string ModuleBin {get {return CombineIfSet(ServerDir,Path.Combine("engine","Modules","Coop","bin","Win64_Shipping_Server"));}}
 public string ConfigPath {get {return Path.Combine(DataDir,"server-config.json");}}
 public string ModConfigPath {get {return Path.Combine(GameUserDir,"CoopData","mod-config.json");}}
 public string SavesDir {get {return Path.Combine(DataDir,"Game Saves");}}
 public static string SettingsPath {get {string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);if(string.IsNullOrEmpty(local))local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","Local");return Path.Combine(local,"BannerlordCoopLauncher","installation.json");}}

 public string[] Validate() {
  var errors=new List<string>();
  if(Environment.OSVersion.Platform!=PlatformID.Win32NT||!Environment.Is64BitOperatingSystem)errors.Add("开服助手需要 64 位 Windows。");
  if(!IsSteamExecutable(SteamExe))errors.Add("未找到可读取的 Steam.exe，请选择本机的 Steam 程序。");
  if(!ReadableFile(GameExecutable(GameDir)))errors.Add("未找到游戏目录中的 bin\\Win64_Shipping_Client\\Bannerlord.exe。");
  if(!ReadableFile(CombineIfSet(ServerDir,"BannerlordCoopServer.exe")))errors.Add("未找到 Bannerlord Coop 专用服务器，请在 Steam 中安装 Coop MOD 或选择 DedicatedServer 文件夹。");
  if(!ReadableFile(CombineIfSet(ModuleBin,"Steamworks.NET.dll")))errors.Add("服务器缺少 Steamworks.NET.dll，请修复 Coop MOD 的服务器组件。");
  if(!ReadableFile(CombineIfSet(ModuleBin,"steam_api64.dll")))errors.Add("服务器缺少 steam_api64.dll，请修复 Coop MOD 的服务器组件。");
  return errors.ToArray();
 }
 static bool ReadableFile(string path) {try {if(string.IsNullOrEmpty(path)||!File.Exists(path))return false;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))return stream.Length>0;}catch{return false;}}
 static bool IsSteamExecutable(string path) {try{return ReadableFile(path)&&string.Equals(Path.GetFileName(path),"Steam.exe",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 static string CombineIfSet(string root,string name) {try{return string.IsNullOrEmpty(root)?"":Path.Combine(root,name);}catch{return "";}}
 static string GameExecutable(string dir) {return CombineIfSet(dir,Path.Combine("bin","Win64_Shipping_Client","Bannerlord.exe"));}
 static string Normalize(string path) {try {return string.IsNullOrWhiteSpace(path)?"":Path.GetFullPath(path.Trim().Trim('"'));}catch{return "";}}
 static void AddPath(List<string> list,string path) {string full=Normalize(path);if(full!=""&&!list.Any(x=>string.Equals(x,full,StringComparison.OrdinalIgnoreCase)))list.Add(full);}

 public static HostInstallation Detect() {
  var candidates=new List<string>();
  ReadRegistry(candidates,Registry.CurrentUser,@"Software\Valve\Steam");
  ReadRegistry(candidates,Registry.LocalMachine,@"SOFTWARE\Valve\Steam");
  ReadRegistry(candidates,Registry.LocalMachine,@"SOFTWARE\WOW6432Node\Valve\Steam");
  try {foreach(var process in Process.GetProcessesByName("steam")){using(process){try {string exe=process.MainModule.FileName;if(string.Equals(Path.GetFileName(exe),"steam.exe",StringComparison.OrdinalIgnoreCase))AddPath(candidates,Path.GetDirectoryName(exe));}catch{}}}}catch{}
  foreach(var special in new[]{Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.ProgramFiles}) {string path=Environment.GetFolderPath(special);if(!string.IsNullOrEmpty(path))AddPath(candidates,Path.Combine(path,"Steam"));}
  string drive=Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));if(!string.IsNullOrEmpty(drive)){AddPath(candidates,Path.Combine(drive,"Steam"));AddPath(candidates,Path.Combine(drive,"Program Files (x86)","Steam"));}
  return DetectCandidates(candidates,null);
 }
 static void ReadRegistry(List<string> candidates,RegistryKey hive,string keyName) {try {using(var key=hive.OpenSubKey(keyName)){if(key==null)return;foreach(string name in new[]{"SteamPath","InstallPath","SteamExe"}){string path=key.GetValue(name) as string;if(string.IsNullOrWhiteSpace(path))continue;if(name=="SteamExe")path=Path.GetDirectoryName(path.Replace('/',Path.DirectorySeparatorChar));AddPath(candidates,path);}}}catch{}}
 internal static HostInstallation DetectCandidates(IEnumerable<string> steamCandidates,string documents) {
  var result=new HostInstallation{DocumentsOverride=documents};var libraries=new List<string>();var games=new List<string>();var servers=new List<string>();
  foreach(var candidate in steamCandidates) {
   string steamRoot=Normalize(candidate);if(steamRoot=="")continue;
   string steam=Path.Combine(steamRoot,"Steam.exe");if(result.SteamExe==""&&ReadableFile(steam))result.SteamExe=steam;
   foreach(var library in LibraryRoots(steamRoot))AddPath(libraries,library);
  }
  foreach(var library in libraries) {
   string appManifest=Path.Combine(library,"steamapps","appmanifest_261550.acf");
   string installDir=ReadInstallDir(appManifest);
   if(installDir!="") {
    // installdir is a folder name below common; reject traversal and rooted paths.
    try {string common=Path.GetFullPath(Path.Combine(library,"steamapps","common"));string game=Path.GetFullPath(Path.Combine(common,installDir));if(game.StartsWith(common.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&ReadableFile(GameExecutable(game)))AddPath(games,game);}catch{}
   }
   string usual=Path.Combine(library,"steamapps","common","Mount & Blade II Bannerlord");if(ReadableFile(GameExecutable(usual)))AddPath(games,usual);
   string workshop=Path.Combine(library,"steamapps","workshop","content","261550");
   AddServer(servers,Path.Combine(workshop,"3770450698","DedicatedServer"));
   // Only this app's immediate workshop items, with a fixed candidate limit.
   try {if(Directory.Exists(workshop))foreach(var item in Directory.EnumerateDirectories(workshop).Take(128))AddServer(servers,Path.Combine(item,"DedicatedServer"));}catch{}
  }
  foreach(var game in games)foreach(var module in new[]{"Coop","BannerlordCoop"})AddServer(servers,Path.Combine(game,"Modules",module,"DedicatedServer"));
  if(games.Count>0)result.GameDir=games[0];
  if(servers.Count>0) {
   // Prefer complete components, keeping an incomplete candidate visible for repair.
   result.ServerDir=servers.FirstOrDefault(s=>ReadableFile(Path.Combine(s,"engine","Modules","Coop","bin","Win64_Shipping_Server","Steamworks.NET.dll"))&&ReadableFile(Path.Combine(s,"engine","Modules","Coop","bin","Win64_Shipping_Server","steam_api64.dll")))??servers[0];
  }
  return result;
 }
 static void AddServer(List<string> servers,string dir) {if(ReadableFile(Path.Combine(dir,"BannerlordCoopServer.exe")))AddPath(servers,dir);}
 internal static List<string> LibraryRoots(string steamRoot) {
  var roots=new List<string>();AddPath(roots,steamRoot);
  string file=Path.Combine(steamRoot,"steamapps","libraryfolders.vdf");
  try {if(!File.Exists(file)||new FileInfo(file).Length>2097152)return roots;foreach(var library in ParseLibraryPaths(File.ReadAllText(file,Utf8)))AddPath(roots,library);}catch{}
  return roots;
 }
 internal static List<string> ParseLibraryPaths(string text) {
  var roots=new List<string>();var root=VdfReader.Parse(text);var libraryFolders=root.Children.FirstOrDefault(x=>x.Key.Equals("libraryfolders",StringComparison.OrdinalIgnoreCase));if(libraryFolders==null)libraryFolders=root;
  foreach(var child in libraryFolders.Children) {
   int index;if(!int.TryParse(child.Key,out index)||index<0)continue;
   if(child.Value!=null) {if(!string.IsNullOrWhiteSpace(child.Value))roots.Add(child.Value);}
   else {var path=child.Children.FirstOrDefault(x=>x.Key.Equals("path",StringComparison.OrdinalIgnoreCase)&&x.Value!=null);if(path!=null&&!string.IsNullOrWhiteSpace(path.Value))roots.Add(path.Value);}
  }
  return roots;
 }
 static string ReadInstallDir(string manifest) {try {if(!File.Exists(manifest)||new FileInfo(manifest).Length>2097152)return "";var root=VdfReader.Parse(File.ReadAllText(manifest,Utf8));var app=root.Children.FirstOrDefault(x=>x.Key.Equals("AppState",StringComparison.OrdinalIgnoreCase));if(app==null)return "";var dir=app.Children.FirstOrDefault(x=>x.Key.Equals("installdir",StringComparison.OrdinalIgnoreCase));return dir==null||dir.Value==null?"":dir.Value;}catch{return "";}}

 public static bool TryLoad(out HostInstallation result) {return TryLoadFrom(SettingsPath,MachineIdentity(),out result);}
 internal static bool TryLoadFrom(string path,string machine,out HostInstallation result) {
  result=null;
  try {
   if(!File.Exists(path)||new FileInfo(path).Length>65536)return false;
   var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Utf8));
   object marker,version;if(json==null||!json.TryGetValue("machineId",out marker)||!(marker is string)||!string.Equals((string)marker,machine,StringComparison.Ordinal)||!json.TryGetValue("schema",out version)||Convert.ToInt32(version)!=1)return false;
   var install=new HostInstallation{SteamExe=Normalize(JsonString(json,"steamExe")),GameDir=Normalize(JsonString(json,"gameDir")),ServerDir=Normalize(JsonString(json,"serverDir"))};
   if(install.Validate().Length!=0)return false;result=install;return true;
  }catch{return false;}
 }
 static string JsonString(Dictionary<string,object> json,string key) {object value;return json.TryGetValue(key,out value)&&value is string?(string)value:"";}
 public void Save() {
  string[] errors=Validate();if(errors.Length!=0)throw new Exception(string.Join("\r\n",errors));
  SaveTo(SettingsPath,MachineIdentity());
 }
 internal void SaveTo(string path,string machine) {
  var content=new Dictionary<string,object>{{"schema",1},{"machineId",machine},{"steamExe",Normalize(SteamExe)},{"gameDir",Normalize(GameDir)},{"serverDir",Normalize(ServerDir)}};
  string folder=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(folder);string temporary=Path.Combine(folder,"installation-"+Guid.NewGuid().ToString("N")+".tmp");
  try {File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(content),Utf8);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
 static string MachineIdentity() {
  string guid="";try {using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")){if(key!=null)guid=Convert.ToString(key.GetValue("MachineGuid"));}}catch{}
  using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(Environment.MachineName+"|"+guid))).Replace("-","");
 }

 internal static void Check(string reportPath,List<string> results) {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"host-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);
  var parsed=ParseLibraryPaths("\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } // newer layout\n \"1\" { \"path\" \"E:\\\\游戏库\\\\带\\\"引号\" } \"2\" \"F:\\\\OldLibrary\" }");
  if(parsed.Count!=3||parsed[0]!=@"C:\Steam"||parsed[1]!="E:\\游戏库\\带\"引号"||parsed[2]!=@"F:\OldLibrary")throw new Exception("VDF paths or escaping incorrect");results.Add("PASS Steam VDF 新旧格式、中文与转义字符，以及跨盘库路径解析");
  string steam=Path.Combine(root,"steam"),first=Path.Combine(root,"lib1"),second=Path.Combine(root,"lib2"),docs=Path.Combine(root,"documents");
  string game=Path.Combine(second,"steamapps","common","Custom Bannerlord"),server=Path.Combine(first,"steamapps","workshop","content","261550","3770450698","DedicatedServer"),module=Path.Combine(server,"engine","Modules","Coop","bin","Win64_Shipping_Server");
  WriteFixture(Path.Combine(steam,"Steam.exe"));WriteFixture(GameExecutable(game));WriteFixture(Path.Combine(server,"BannerlordCoopServer.exe"));WriteFixture(Path.Combine(module,"Steamworks.NET.dll"));WriteFixture(Path.Combine(module,"steam_api64.dll"));
  WriteText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+EscapeVdf(first)+"\" } \"1\" { \"path\" \""+EscapeVdf(second)+"\" } }");
  WriteText(Path.Combine(second,"steamapps","appmanifest_261550.acf"),"\"AppState\" { \"appid\" \"261550\" \"installdir\" \"Custom Bannerlord\" }");
  var found=DetectCandidates(new[]{steam},docs);if(found.SteamExe!=Path.Combine(steam,"Steam.exe")||found.GameDir!=game||found.ServerDir!=server||found.Validate().Length!=0||found.GameUserDir!=Path.Combine(docs,"Mount and Blade II Bannerlord"))throw new Exception("Multi-library installation detection incorrect");results.Add("PASS 游戏与 MOD 分处多个 Steam 库、自定义安装目录及当前电脑文档目录适配");
  string settings=Path.Combine(root,"settings","installation.json");found.SaveTo(settings,"fixture-machine");HostInstallation loaded;
  if(!TryLoadFrom(settings,"fixture-machine",out loaded)||loaded.GameDir!=game)throw new Exception("Saved paths did not reload");
  if(TryLoadFrom(settings,"other-machine",out loaded))throw new Exception("Cross-machine settings were accepted");
  File.Delete(Path.Combine(module,"steam_api64.dll"));if(TryLoadFrom(settings,"fixture-machine",out loaded)||!found.Validate().Any(x=>x.Contains("steam_api64.dll")))throw new Exception("Missing Steam component was accepted");
  File.Delete(Path.Combine(game,"bin","Win64_Shipping_Client","Bannerlord.exe"));if(!found.Validate().Any(x=>x.Contains("Bannerlord.exe")))throw new Exception("Missing game executable was accepted");results.Add("PASS 本机设置重载、跨电脑设置失效以及游戏或 Steam 组件缺失检测");
  WriteFixture(GameExecutable(game));WriteFixture(Path.Combine(module,"steam_api64.dll"));WriteText(Path.Combine(second,"steamapps","appmanifest_261550.acf"),"\"AppState\" { \"installdir\" \"..\\\\..\\\\outside\" }");if(DetectCandidates(new[]{steam},docs).GameDir!="")throw new Exception("Unsafe manifest installdir was accepted");results.Add("PASS 安装清单的目录越界路径被拒绝，检测不扫描全盘");
  var actual=Detect();results.Add("READ-ONLY 当前电脑自动检测：Steam="+(actual.SteamExe!=""?"找到":"未找到")+"；游戏="+(actual.GameDir!=""?"找到":"未找到")+"；Coop 服务器="+(actual.ServerDir!=""?"找到":"未找到")+"；组件检查="+(actual.Validate().Length==0?"通过":"需要适配"));
 }
 static string EscapeVdf(string value) {return value.Replace("\\","\\\\").Replace("\"","\\\"");}
 static void WriteFixture(string path) {WriteText(path,"fixture");}
 static void WriteText(string path,string text) {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text,Utf8);}

 sealed class VdfNode {internal string Key,Value;internal List<VdfNode> Children=new List<VdfNode>();}
 sealed class VdfReader {
  readonly string text;int position;
  VdfReader(string source) {text=source??"";if(text.Length>2097152)throw new Exception("VDF exceeds the permitted size");}
  internal static VdfNode Parse(string source) {var reader=new VdfReader(source);var root=new VdfNode();reader.ReadNodes(root,false,0);return root;}
  void ReadNodes(VdfNode parent,bool needsEnd,int depth) {
   if(depth>32)throw new Exception("VDF nesting exceeds the permitted depth");
   while(true) {string key=Token();if(key==null){if(needsEnd)throw new Exception("Unclosed VDF section");return;}if(key=="}"){if(!needsEnd)throw new Exception("Unexpected VDF section end");return;}if(key=="{")throw new Exception("VDF section requires a key");string value=Token();if(value==null||value=="}")throw new Exception("Missing VDF value");var node=new VdfNode{Key=key};if(value=="{")ReadNodes(node,true,depth+1);else node.Value=value;parent.Children.Add(node);}
  }
  string Token() {
   while(position<text.Length) {if(char.IsWhiteSpace(text[position])||text[position]=='\uFEFF'){position++;continue;}if(position+1<text.Length&&text[position]=='/'&&text[position+1]=='/'){while(position<text.Length&&text[position]!='\n')position++;continue;}break;}
   if(position>=text.Length)return null;char first=text[position++];if(first=='{'||first=='}')return first.ToString();var value=new StringBuilder();
   if(first=='"') {while(position<text.Length){char c=text[position++];if(c=='"')return value.ToString();if(c=='\\'&&position<text.Length){char next=text[position++];if(next=='\\'||next=='"')value.Append(next);else if(next=='n')value.Append('\n');else if(next=='t')value.Append('\t');else if(next=='r')value.Append('\r');else {value.Append('\\');value.Append(next);}}else value.Append(c);}throw new Exception("Unclosed VDF string");}
   value.Append(first);while(position<text.Length&&!char.IsWhiteSpace(text[position])&&text[position]!='{'&&text[position]!='}')value.Append(text[position++]);return value.ToString();
  }
 }
}
