using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class CoopLauncher {
 readonly System.Windows.Forms.Timer closeTimer=new System.Windows.Forms.Timer();
 Process startedServer,closingServer;
 bool closeRequested,allowFinalClose;
 LauncherShutdown shutdown;

 void RequestLauncherClose(object sender,FormClosingEventArgs e) {
  if(preview||allowFinalClose)return;
  e.Cancel=true;
  if(closeRequested)return;
  bool stopAlreadyRequested=pendingRestart!=null;pendingRestart=null;
  try {
   closingServer=startedServer!=null&&!startedServer.HasExited?startedServer:(FindServer()??startedServer);
   closeRequested=true;
   shutdown=new LauncherShutdown(
    ()=>{if(closingServer!=null)ConsoleInput.Send((uint)closingServer.Id,"stop");},
    ()=>closingServer==null||closingServer.HasExited,
    ()=>closingServer!=null&&PortInUse(),DateTime.UtcNow.AddSeconds(90),stopAlreadyRequested);
   closeTimer.Interval=250;closeTimer.Tick+=PollLauncherClose;closeTimer.Start();
   DisableClosingControls();info.Text="正在保存并停止服务器，确认退出后自动关闭助手。";
  }catch(Exception error){CancelLauncherClose("无法确认服务器是否已停止："+error.Message);}
 }

 void DisableClosingControls() {
  tabs.Enabled=false;start.Enabled=saveNow.Enabled=restart.Enabled=stop.Enabled=exportSave.Enabled=deleteSave.Enabled=renameSave.Enabled=false;
 }
 void PollLauncherClose(object sender,EventArgs e) {
  if(!closeRequested)return;
  DisableClosingControls();
  var result=shutdown.Poll(DateTime.UtcNow,pendingInstantSave!=null||pendingExport!=null);
  if(result==ShutdownResult.Complete){closeTimer.Stop();allowFinalClose=true;LogEvent("关闭助手：服务器已停止");Close();return;}
  if(result==ShutdownResult.TimedOut){
   CancelLauncherClose("等待服务器保存并停止超时，助手保持打开，未强制结束服务器。请在“服务器控制台”检查日志或交互提示后重试。"+(string.IsNullOrEmpty(shutdown.LastError)?"":"\r\n"+shutdown.LastError));return;
  }
  info.Text=pendingInstantSave!=null||pendingExport!=null?"正在完成当前保存或导出，完成后停止服务器并关闭助手。":"正在保存并停止服务器，确认退出后自动关闭助手。";
 }
 void CancelLauncherClose(string message) {
  closeTimer.Stop();closeTimer.Tick-=PollLauncherClose;closeRequested=false;shutdown=null;tabs.Enabled=true;
  if(closingServer!=null&&closingServer!=startedServer)closingServer.Dispose();closingServer=null;
  RefreshStatus();info.Text=message;MessageBox.Show(this,message,"关闭未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
 }
 void DisposeShutdown() {
  closeTimer.Dispose();
  if(closingServer!=null&&closingServer!=startedServer)closingServer.Dispose();
  if(startedServer!=null)startedServer.Dispose();
 }
}

internal enum ShutdownResult {Waiting,Complete,TimedOut}
internal sealed class LauncherShutdown {
 readonly Action sendStop;readonly Func<bool> hasExited,portInUse;readonly DateTime deadline;
 bool stopRequested;internal string LastError="";
 internal LauncherShutdown(Action send,Func<bool> exited,Func<bool> port,DateTime end,bool alreadyRequested) {sendStop=send;hasExited=exited;portInUse=port;deadline=end;stopRequested=alreadyRequested;}
 internal ShutdownResult Poll(DateTime now,bool saveBusy) {
  try {
   // Finish pending save/export operations before closing or asking the server to stop.
   if(!saveBusy){
    bool exited=hasExited();
    if(exited&&!portInUse())return ShutdownResult.Complete;
    if(now>=deadline)return ShutdownResult.TimedOut;
    if(!exited&&!stopRequested){sendStop();stopRequested=true;LastError="";}
   }
  }catch(Exception error){LastError=error.Message;}
  return now>=deadline?ShutdownResult.TimedOut:ShutdownResult.Waiting;
 }
}

internal static class LauncherShutdownChecks {
 internal static void Check(string reportPath,List<string> results) {
  var now=DateTime.UtcNow;int sent=0;bool exited=false,port=true;
  var shutdown=new LauncherShutdown(()=>sent++,()=>exited,()=>port,now.AddSeconds(90),false);
  if(shutdown.Poll(now,true)!=ShutdownResult.Waiting||sent!=0)throw new Exception("Shutdown interrupted an active save");
  if(shutdown.Poll(now,false)!=ShutdownResult.Waiting||sent!=1||shutdown.Poll(now.AddSeconds(1),false)!=ShutdownResult.Waiting||sent!=1)throw new Exception("Shutdown stop request not sent exactly once");
  exited=true;if(shutdown.Poll(now.AddSeconds(2),false)!=ShutdownResult.Waiting)throw new Exception("Shutdown ignored live server port");
  port=false;if(shutdown.Poll(now.AddSeconds(3),true)!=ShutdownResult.Waiting||shutdown.Poll(now.AddSeconds(3),false)!=ShutdownResult.Complete)throw new Exception("Shutdown did not await completed export and server exit");
  results.Add("PASS 关闭先等待保存/导出完成，仅发送一次停服命令，进程退出且端口释放后才允许关闭");
  var restarting=new LauncherShutdown(()=>sent++,()=>false,()=>true,now.AddSeconds(90),true);
  restarting.Poll(now,false);if(sent!=1||restarting.Poll(now.AddSeconds(91),false)!=ShutdownResult.TimedOut)throw new Exception("Pending restart shutdown or timeout incorrect");
  var unavailable=new LauncherShutdown(()=>{throw new IOException("not ready");},()=>false,()=>true,now.AddSeconds(1),false);
  if(unavailable.Poll(now,false)!=ShutdownResult.Waiting||unavailable.Poll(now.AddSeconds(2),false)!=ShutdownResult.TimedOut||unavailable.LastError!="not ready")throw new Exception("Shutdown input failure not retained");
  results.Add("PASS 已请求停服时不重复发送，停服超时或输入失败不报告退出、不强制结束进程");
  string marker=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"shutdown-"+Guid.NewGuid().ToString("N")+".txt");
  using(var fixture=Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location,"--console-fixture \""+marker+"\""){UseShellExecute=false,CreateNoWindow=false,WindowStyle=ProcessWindowStyle.Hidden})){
   try {
    ServerConsoleChecks.WaitFor(fixture,"FIXTURE 中文服务器日志");
    var actual=new LauncherShutdown(()=>ConsoleInput.Send((uint)fixture.Id,"stop"),()=>fixture.HasExited,()=>false,DateTime.UtcNow.AddSeconds(10),false);
    ShutdownResult state;
    do{state=actual.Poll(DateTime.UtcNow,false);if(state==ShutdownResult.Waiting)Thread.Sleep(100);}while(state==ShutdownResult.Waiting);
    if(state!=ShutdownResult.Complete||fixture.ExitCode!=0||!File.Exists(marker)||File.ReadAllText(marker)!="saved")throw new Exception("Native shutdown did not persist data and exit before close");
    results.Add("PASS Windows 隔离服务器收到 stop 后先写入保存标记并退出，关闭流程等待保存及退出完成");
   }finally{if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit();}}
  }
 }
}
