using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed partial class CoopLauncher {
 PendingInstantSave pendingInstantSave;
 internal bool IsInstantSavePending {get{return pendingInstantSave!=null;}}

 void BeginInstantSave() {
  if(preview)throw new Exception("预览模式不能保存正在运行的战役。");
  if(pendingInstantSave!=null||pendingRestart!=null||pendingExport!=null)throw new Exception("请等待当前保存任务结束。");
  RefreshStatus();
  using(var p=FindServer()) {
   if(p==null||!ready||string.IsNullOrEmpty(currentLog)||!ValidSaveName(runningSave))throw new Exception("请等待服务器战役就绪，再点“立刻保存”。");
   // Save the authoritative running world, even when the user has selected another world in the list.
   var job=InstantSaveConfirmation.Capture(p.Id,p.StartTime.ToUniversalTime(),runningSave,currentLog,SavesDir,DateTime.UtcNow);
   pendingInstantSave=job;
   try{ConsoleInput.Send((uint)p.Id,"save");}
   catch {pendingInstantSave=null;throw;}
   tabs.Enabled=false;
   info.Text="正在保存当前战役“"+job.Name+"”，请等待保存结果。服务器继续运行。";
   LogEvent("请求立刻保存当前战役 "+job.Name);
  }
 }

 // Call after ReadRunningState, and once with null before clearing state when the server disappears.
 void PollInstantSave(Process p,string log) {
  var job=pendingInstantSave;if(job==null)return;
  try {
   if(p==null||p.Id!=job.ProcessId||p.StartTime.ToUniversalTime()!=job.ProcessStartedUtc) {
    FinishInstantSave("服务器进程已改变，未确认本次保存完成。请查看存档和服务器日志后重试。",false);return;
   }
   if(!string.Equals(currentLog,job.LogPath,StringComparison.OrdinalIgnoreCase)||!string.Equals(runningSave,job.Name,StringComparison.Ordinal)) {
    FinishInstantSave("当前战役或日志已改变，未确认本次保存完成。请等待服务器就绪后重试。",false);return;
   }
   string fresh,changed;
   if(!InstantSaveConfirmation.TryReadTail(job,out fresh,out changed)) {
    FinishInstantSave(changed+"未确认本次保存完成，请查看服务器日志。",false);return;
   }
   if(InstantSaveConfirmation.HasSaveFailure(fresh,job.Name)) {
    FinishInstantSave("服务器报告本次保存异常。未确认战役与角色记录均已保存，请查看服务器日志后重试。",false);return;
   }
   if(InstantSaveConfirmation.HasWorldSaved(fresh,job.Name)) {
    string pairError;
    if(InstantSaveConfirmation.PairIsPersisted(job,out pairError)) {
     FinishInstantSave("当前战役“"+job.Name+"”已保存，世界和联机角色记录均已写入电脑。服务器继续运行。",true);return;
    }
    job.LastPairError=pairError;
   }
   if(DateTime.UtcNow>=job.DeadlineUtc) {
    FinishInstantSave("等待保存结果超时，未确认本次保存完成。"+(string.IsNullOrEmpty(job.LastPairError)?"":job.LastPairError)+"请查看服务器日志后重试。",false);
   }
  } catch(Exception e) {
   // A transient sharing violation while saving is not evidence of save loss.
   if(DateTime.UtcNow>=job.DeadlineUtc)FinishInstantSave("无法确认本次保存结果："+e.Message+"。请查看服务器日志后重试。",false);
  }
 }
 void FinishInstantSave(string message,bool completed) {
  var job=pendingInstantSave;pendingInstantSave=null;tabs.Enabled=pendingRestart==null&&pendingExport==null;
  info.Text=message;if(job!=null)LogEvent((completed?"立刻保存已完成 ":"立刻保存未确认 ")+job.Name+"："+message);
 }
 void PollInstantSaveReadError() {
  if(pendingInstantSave==null)return;
  try{using(var process=FindServer())PollInstantSave(process,"");}
  catch(Exception error){if(pendingInstantSave!=null&&DateTime.UtcNow>=pendingInstantSave.DeadlineUtc)FinishInstantSave("无法确认本次保存结果："+error.Message+"。请查看服务器日志后重试。",false);}
 }
}

internal sealed class PendingInstantSave {
 internal string Name,LogPath,SavesPath,LastPairError,PreviousUniqueGameId;
 internal int ProcessId;
 internal long LogOffset,LastSeenLogLength;
 internal byte[] LogAnchor;
 internal DateTime LogCreatedUtc,ProcessStartedUtc,RequestedUtc,DeadlineUtc;
 internal long PreviousWorldWriteTicks,PreviousSessionWriteTicks;
}

internal static class InstantSaveConfirmation {
 internal static PendingInstantSave Capture(int pid,DateTime startedUtc,string name,string logPath,string savesPath,DateTime nowUtc) {
  if(pid<=0||!CoopLauncher.ValidSaveName(name))throw new Exception("无法确定当前服务器战役，保存请求已取消。");
  var job=new PendingInstantSave{Name=name,ProcessId=pid,ProcessStartedUtc=startedUtc,LogPath=Path.GetFullPath(logPath),SavesPath=Path.GetFullPath(savesPath),RequestedUtc=nowUtc,DeadlineUtc=nowUtc.AddSeconds(90)};
  using(var stream=new FileStream(job.LogPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
   job.LogOffset=job.LastSeenLogLength=stream.Length;int count=(int)Math.Min(128,stream.Length);job.LogAnchor=new byte[count];
   stream.Seek(job.LogOffset-count,SeekOrigin.Begin);ReadExactly(stream,job.LogAnchor);
  }
  job.LogCreatedUtc=File.GetCreationTimeUtc(job.LogPath);
  job.PreviousWorldWriteTicks=WriteTicks(Path.Combine(job.SavesPath,name+".sav"));
  job.PreviousSessionWriteTicks=WriteTicks(Path.Combine(job.SavesPath,name+".json"));
  // The campaign identity can survive a filename change; it is not necessarily the displayed save name.
  job.PreviousUniqueGameId=ReadExistingUniqueGameId(Path.Combine(job.SavesPath,name+".json"));
  return job;
 }
 static string ReadExistingUniqueGameId(string sessionPath) {
  if(!File.Exists(sessionPath))return "";
  try {
   using(var stream=new FileStream(sessionPath,FileMode.Open,FileAccess.Read,FileShare.Read))using(var reader=new StreamReader(stream,Encoding.UTF8)) {
    var serializer=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var data=serializer.DeserializeObject(reader.ReadToEnd()) as Dictionary<string,object>;
    return data!=null&&data.ContainsKey("UniqueGameId")?Convert.ToString(data["UniqueGameId"]):"";
   }
  }catch{return "";}
 }
 static long WriteTicks(string path){return File.Exists(path)?File.GetLastWriteTimeUtc(path).Ticks:0;}
 static void ReadExactly(Stream stream,byte[] buffer) {
  int done=0;while(done<buffer.Length){int read=stream.Read(buffer,done,buffer.Length-done);if(read==0)throw new IOException("日志读取未完成，请稍后重试。");done+=read;}
 }
 internal static bool TryReadTail(PendingInstantSave job,out string fresh,out string changed) {
  fresh="";changed="";
  if(!File.Exists(job.LogPath)){changed="服务器日志已被移除，";return false;}
  if(File.GetCreationTimeUtc(job.LogPath)!=job.LogCreatedUtc){changed="服务器日志已被替换，";return false;}
  using(var stream=new FileStream(job.LogPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
   long length=stream.Length;if(length<job.LogOffset||length<job.LastSeenLogLength){changed="服务器日志已被截断，";return false;}
   var anchor=new byte[job.LogAnchor.Length];stream.Seek(job.LogOffset-anchor.Length,SeekOrigin.Begin);ReadExactly(stream,anchor);
   for(int i=0;i<anchor.Length;i++)if(anchor[i]!=job.LogAnchor[i]){changed="服务器日志内容已改变，";return false;}
   stream.Seek(job.LogOffset,SeekOrigin.Begin);using(var reader=new StreamReader(stream,Encoding.UTF8)){fresh=reader.ReadToEnd();}
   job.LastSeenLogLength=length;return true;
  }
 }
 internal static bool HasWorldSaved(string fresh,string name) {
  // v0.1.6 reports builtin `save` as operator save; older hosts may use their autosave path.
  return Regex.IsMatch(fresh,@"^\d\d:\d\d:\d\d\.\d+[ \t]+\[DedicatedServer\][ \t]+(?:operator save|autosave):[ \t]+world saved to '"+Regex.Escape(name+".sav")+@"'[ \t]+\(\d+ ms\)[ \t]*\r?$",RegexOptions.Multiline);
 }
 internal static bool HasSaveFailure(string fresh,string name) {
  if(Regex.IsMatch(fresh,@"^\d\d:\d\d:\d\d\.\d+[ \t]+\[DedicatedServer\][ \t]+(?:operator save|autosave):[^\r\n]*(?:fail|error|timed out|timeout|abort)",RegexOptions.Multiline|RegexOptions.IgnoreCase))return true;
  return Regex.IsMatch(fresh,@"^\d\d:\d\d:\d\d\.\d+[^\r\n]*Failed to write the co-op session for save[ \t]+[""']?"+Regex.Escape(name)+@"[""']?(?:[ \t\r\n]|$)",RegexOptions.Multiline|RegexOptions.IgnoreCase);
 }
 internal static bool PairIsPersisted(PendingInstantSave job,out string error) {
  error="";string world=Path.Combine(job.SavesPath,job.Name+".sav"),session=Path.Combine(job.SavesPath,job.Name+".json");
  if(!File.Exists(world)||!File.Exists(session)){error="尚未确认完整的世界与角色记录。";return false;}
  long worldTicks=WriteTicks(world),sessionTicks=WriteTicks(session);
  if(worldTicks<=job.PreviousWorldWriteTicks||sessionTicks<=job.PreviousSessionWriteTicks||worldTicks<job.RequestedUtc.Ticks||sessionTicks<job.RequestedUtc.Ticks) {
   error="尚未确认世界和联机角色记录都是本次保存的文件。";return false;
  }
  try {
   // FileShare.Read forbids an overlapping writer; keep both reads open while validating the pair.
   using(var worldStream=new FileStream(world,FileMode.Open,FileAccess.Read,FileShare.Read))
   using(var sessionStream=new FileStream(session,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(worldStream.Length==0||sessionStream.Length==0){error="世界或角色记录仍为空。";return false;}
    string text;using(var reader=new StreamReader(sessionStream,Encoding.UTF8)){text=reader.ReadToEnd();}
    var serializer=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};var data=serializer.DeserializeObject(text) as Dictionary<string,object>;
    string uniqueId=data!=null&&data.ContainsKey("UniqueGameId")?Convert.ToString(data["UniqueGameId"]):"";
    bool identityMatches=string.IsNullOrEmpty(job.PreviousUniqueGameId)||string.Equals(uniqueId,job.PreviousUniqueGameId,StringComparison.Ordinal)||string.Equals(uniqueId,job.Name,StringComparison.Ordinal);
    if(data==null||string.IsNullOrWhiteSpace(uniqueId)||!identityMatches||!data.ContainsKey("Players")||!(data["Players"] is object[])) {
     error="联机角色记录尚未完整写入对应战役。";return false;
    }
   }
   return true;
  }catch(IOException){error="存档仍在写入，请等待。";return false;}
   catch(UnauthorizedAccessException){error="暂时无法读取保存结果。";return false;}
   catch(ArgumentException){error="角色记录尚未完整写入。";return false;}
   catch(InvalidOperationException){error="角色记录尚未完整写入。";return false;}
 }
 internal static void Check(string reportPath,List<string> results) {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"instant-save-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string name="测试战役(即时)",log=Path.Combine(root,"server.log");var utf8=new UTF8Encoding(false);
  string line="20:10:01.234  [DedicatedServer] autosave: world saved to '"+name+".sav' (1273 ms)\r\n";
  File.WriteAllText(log,line,utf8);var job=Capture(123,DateTime.UtcNow,name,log,root,DateTime.UtcNow);string fresh,changed;
  if(!TryReadTail(job,out fresh,out changed)||HasWorldSaved(fresh,name))throw new Exception("Earlier save incorrectly confirmed instant save");
  File.AppendAllText(log,"20:10:02.234  [DedicatedServer] autosave: world saved to '另一个战役.sav' (123 ms)\r\n20:10:03.234  [DedicatedServer Engine] ------Successfully saved------\r\nhelp: save\r\n",utf8);
  if(!TryReadTail(job,out fresh,out changed)||HasWorldSaved(fresh,name))throw new Exception("Another world or help confirmed instant save");
  File.AppendAllText(log,line,utf8);if(!TryReadTail(job,out fresh,out changed)||!HasWorldSaved(fresh,name))throw new Exception("Matching fresh save not recognised");
  string operatorLine=line.Replace("autosave:","operator save:");if(!HasWorldSaved(operatorLine,name)||!HasSaveFailure("20:10:01.234 [DedicatedServer] operator save: save failed\r\n",name))throw new Exception("Real dedicated operator save result not recognised");
  results.Add("PASS 立刻保存只认本次请求后对应世界的完整保存日志，旧日志、其他世界、帮助与排队提示不误报");
  if(HasWorldSaved(line.TrimEnd('\r','\n')+" failed",name)||HasWorldSaved(line.Replace("autosave:","shutdown save:"),name))throw new Exception("Incomplete or shutdown result confirmed instant save");
  if(!HasSaveFailure("20:10:01.234 [DedicatedServer] autosave: save failed\r\n",name)||!HasSaveFailure("20:10:01.234 [Coop] Failed to write the co-op session for save \""+name+"\"\r\n",name)||HasSaveFailure("20:10:01.234 [Coop] Failed to write the co-op session for save \"other\"\r\n",name))throw new Exception("Save failure recognition incorrect");
  File.WriteAllText(log,"truncated",utf8);if(TryReadTail(job,out fresh,out changed))throw new Exception("Truncated log accepted");
  results.Add("PASS 日志截断、非保存成功行和明确保存异常阻止成功提示");
  string world=Path.Combine(root,name+".sav"),session=Path.Combine(root,name+".json");File.WriteAllText(world,"world",utf8);File.WriteAllText(session,"{\"UniqueGameId\":\""+name+"\",\"Players\":[]}",utf8);
  DateTime later=job.RequestedUtc.AddSeconds(2);File.SetLastWriteTimeUtc(world,later);File.SetLastWriteTimeUtc(session,later);string error;
  if(!PairIsPersisted(job,out error))throw new Exception("Complete fresh save pair blocked: "+error);
  File.SetLastWriteTimeUtc(session,job.RequestedUtc.AddSeconds(-2));if(PairIsPersisted(job,out error))throw new Exception("Stale session accepted");File.SetLastWriteTimeUtc(session,later);
  using(var locked=new FileStream(session,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite)){if(PairIsPersisted(job,out error))throw new Exception("Session still being written accepted");}
  job.PreviousUniqueGameId="旧战役固定标识";File.WriteAllText(session,"{\"UniqueGameId\":\"旧战役固定标识\",\"Players\":[]}",utf8);File.SetLastWriteTimeUtc(session,later);if(!PairIsPersisted(job,out error))throw new Exception("Renamed world identity blocked: "+error);
  File.WriteAllText(session,"{\"UniqueGameId\":\""+name+"\",\"Players\":[]}",utf8);File.SetLastWriteTimeUtc(session,later);if(!PairIsPersisted(job,out error))throw new Exception("Renamed world identity normalisation blocked: "+error);
  File.WriteAllText(session,"{\"UniqueGameId\":\"other\",\"Players\":[]}",utf8);File.SetLastWriteTimeUtc(session,later);if(PairIsPersisted(job,out error))throw new Exception("Wrong stable world identity accepted");
  File.WriteAllText(session,"{\"UniqueGameId\":",utf8);File.SetLastWriteTimeUtc(session,later);if(PairIsPersisted(job,out error))throw new Exception("Incomplete session accepted");
  results.Add("PASS 完成提示要求新写入的世界和对应联机角色 JSON；旧记录、错误战役、未写完或占用中的记录不会误报，改名后保留的战役标识可正常保存");
 }
}
