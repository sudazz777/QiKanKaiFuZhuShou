using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

// Own the redirected streams of servers launched without any console window.
internal sealed class ServerPipeSession : IDisposable {
 static readonly object RegistryGate=new object();
 static readonly Dictionary<uint,ServerPipeSession> Sessions=new Dictionary<uint,ServerPipeSession>();
 readonly object outputGate=new object(),inputGate=new object();
 readonly Process process;readonly StreamWriter input;readonly Thread outputThread,errorThread;
 readonly StringBuilder output=new StringBuilder();
 volatile bool disposed;
 ServerPipeSession(Process child) {
  process=child;input=new StreamWriter(child.StandardInput.BaseStream,new UTF8Encoding(false)){AutoFlush=true};
  outputThread=new Thread(()=>Pump(child.StandardOutput,"标准输出")){IsBackground=true};
  errorThread=new Thread(()=>Pump(child.StandardError,"错误输出")){IsBackground=true};
 }
 internal static void Register(Process child) {
  var session=new ServerPipeSession(child);uint pid=(uint)child.Id;
  lock(RegistryGate){if(Sessions.ContainsKey(pid))throw new InvalidOperationException("服务器输出已连接。");Sessions.Add(pid,session);}
  session.outputThread.Start();session.errorThread.Start();
 }
 static ServerPipeSession Get(uint pid){lock(RegistryGate){ServerPipeSession session;return Sessions.TryGetValue(pid,out session)?session:null;}}
 internal static bool Contains(uint pid){return Get(pid)!=null;}
 internal static bool TryRead(uint pid,out string snapshot) {
  var session=Get(pid);snapshot="";if(session==null)return false;
  lock(session.outputGate)snapshot=session.output.ToString();return true;
 }
 internal static bool TrySend(uint pid,string command) {
  var session=Get(pid);if(session==null)return false;
  lock(session.inputGate){if(session.disposed||session.process.HasExited)throw new IOException("服务器已退出，命令未发送。");session.input.WriteLine(command);}
  return true;
 }
 internal static void Remove(uint pid) {
  ServerPipeSession session;lock(RegistryGate){if(!Sessions.TryGetValue(pid,out session))return;Sessions.Remove(pid);}
  session.Dispose();
 }
 void Append(string text){lock(outputGate){output.Append(text);if(output.Length>24000)output.Remove(0,output.Length-24000);}}
 void Pump(StreamReader reader,string name) {
  try{char[] buffer=new char[2048];int count;while(!disposed&&(count=reader.Read(buffer,0,buffer.Length))>0)Append(new string(buffer,0,count));}
  catch(Exception error){if(!disposed)Append("\r\n[助手] "+name+"读取中断："+error.Message+"\r\n");}
 }
 public void Dispose() {
  disposed=true;lock(inputGate){try{input.Dispose();}catch{}}
  try{process.StandardOutput.Close();}catch{}try{process.StandardError.Close();}catch{}
  outputThread.Join(200);errorThread.Join(200);
 }
}

internal static class ServerPipeChecks {
 internal static void RunFixture(string marker) {
  // No AllocConsole: stdin/stdout remain pipes even when Windows Terminal is the default host.
  Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
  Console.Error.WriteLine("STDERR 模拟错误输出");Console.Write("PIPE 中文日志，请输入命令：");
  string command;while((command=Console.ReadLine())!=null){
   Console.WriteLine("COMMAND "+command);
   if(command=="stop"){Thread.Sleep(250);File.WriteAllText(marker,"saved");return;}
  }
  Environment.ExitCode=1;
 }
 static string WaitFor(Process process,string expected) {
  var deadline=DateTime.UtcNow.AddSeconds(10);string snapshot="";
  while(DateTime.UtcNow<deadline){if(ServerPipeSession.TryRead((uint)process.Id,out snapshot)&&snapshot.Contains(expected))return snapshot;if(process.HasExited)break;Thread.Sleep(50);}
  throw new Exception("Pipe output not received: "+expected+" / "+snapshot);
 }
 internal static void Check(string reportPath,List<string> results) {
  string marker=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"pipe-save-"+Guid.NewGuid().ToString("N")+".txt");
  var launch=new ProcessStartInfo(typeof(Program).Assembly.Location,"--pipe-fixture \""+marker+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  using(var fixture=Process.Start(launch)){
   uint pid=(uint)fixture.Id;ServerPipeSession.Register(fixture);
   try{
    WaitFor(fixture,"PIPE 中文日志，请输入命令：");WaitFor(fixture,"STDERR 模拟错误输出");
    if(ConsoleInput.HasConsole(pid))throw new Exception("Windowless fixture unexpectedly has a console");
    results.Add("PASS 无窗口启动不分配控制台，管道实时读取中文标准/错误输出及无换行的交互提示");
    ConsoleInput.Send(pid,"中文命令");WaitFor(fixture,"COMMAND 中文命令");
    results.Add("PASS 无控制台时通过 UTF-8 输入管道发送命令并收到响应，不依赖终端或控制台按键");
    var shutdown=new LauncherShutdown(()=>ConsoleInput.Send(pid,"stop"),()=>fixture.HasExited,()=>false,DateTime.UtcNow.AddSeconds(10),false);
    ShutdownResult state;do{state=shutdown.Poll(DateTime.UtcNow,false);if(state==ShutdownResult.Waiting)Thread.Sleep(50);}while(state==ShutdownResult.Waiting);
    if(state!=ShutdownResult.Complete||fixture.ExitCode!=0||!File.Exists(marker)||File.ReadAllText(marker)!="saved")throw new Exception("Windowless server shutdown failed to persist data");
    results.Add("PASS 无窗口服务器收到 stop 后先保存再退出，助手关闭流程等待其完成");
   }finally{if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit();}ServerPipeSession.Remove(pid);}
   if(ServerPipeSession.Contains(pid))throw new Exception("Exited server stream registration leaked");
  }
 }
}
