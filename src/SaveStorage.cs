using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal static class SaveStorage {
 static readonly string[] Extensions={".sav",".json",".backup1.sav",".backup1.json",".backup2.sav",".backup2.json"};
 static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
 static string ManagedName(string savePath,string managedDir) {
  string full=Path.GetFullPath(savePath),root=Path.GetFullPath(managedDir).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
  if(!string.Equals(Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),root,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(full),".sav",StringComparison.OrdinalIgnoreCase))throw new Exception("只能管理服务器存档目录中的战役副本。");
  string name=Path.GetFileNameWithoutExtension(full);if(!CoopLauncher.ValidSaveName(name))throw new Exception("不能导出、改名或删除初始模板、传输文件及保留的内部文件。");
  if(!File.Exists(full)||new FileInfo(full).Length==0)throw new Exception("存档尚未生成或世界文件为空，请先开服并保存。");return name;
 }
 static string[] Files(string dir,string name) {
  string root=Path.GetFullPath(dir);var list=new List<string>();
  foreach(var extension in Extensions){string path=Path.GetFullPath(Path.Combine(root,name+extension));RequireChild(path,root);if(File.Exists(path))list.Add(path);}return list.ToArray();
 }
 static void RequireChild(string path,string root) {
  string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!Path.GetFullPath(path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("存档路径越出了允许目录，操作已取消。");
 }
 static string NewFolder(string root,string prefix) {
  string folder=Path.GetFullPath(Path.Combine(root,prefix+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)));RequireChild(folder,root);
  if(Directory.Exists(folder))throw new Exception("备份目录已存在，请重试。");Directory.CreateDirectory(folder);return folder;
 }
 static List<FileStream> LockFiles(string[] files,bool allowMove) {
  var streams=new List<FileStream>();try{foreach(var file in files)streams.Add(new FileStream(file,FileMode.Open,FileAccess.Read,allowMove?FileShare.Read|FileShare.Delete:FileShare.Read));return streams;}catch{foreach(var stream in streams)stream.Dispose();throw new Exception("存档正在写入或被占用，请等待保存完成后重试。");}
 }
 internal static string Export(string savePath,string managedDir,string desktopDir) {
  string name=ManagedName(savePath,managedDir);string[] files=Files(managedDir,name);
  if(files.Any(f=>string.Equals(f,Path.ChangeExtension(savePath,".json"),StringComparison.OrdinalIgnoreCase)&&new FileInfo(f).Length==0))throw new Exception("联机角色记录为空，请先完整保存后再导出。");
  var streams=LockFiles(files,false);string folder="";
  try {
   folder=NewFolder(desktopDir,"骑砍2存档-"+name);
   for(int i=0;i<files.Length;i++) {string dest=Path.Combine(folder,Path.GetFileName(files[i]));RequireChild(dest,folder);using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None)){streams[i].CopyTo(output);output.Flush(true);}}
   File.WriteAllText(Path.Combine(folder,"存档导入说明.txt"),"战役："+name+"\r\n导出时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n\r\n已复制世界文件及存在的同名联机角色记录和历史备份。\r\n导入时选择 "+name+".sav，并保留同名 .json 在其旁边。\r\n不要仅恢复 .sav 而遗漏 .json，否则原来的联机角色进度无法一起恢复。\r\n"+(File.Exists(Path.Combine(managedDir,name+".json"))?"":"此世界没有同名联机角色记录。\r\n"),Utf8);
   return folder;
  } catch(Exception error) {
   if(folder!=""){try{File.WriteAllText(Path.Combine(folder,"导出未完成.txt"),"本次导出未完成，请勿用它恢复战役。原存档保留。\r\n"+error.Message,Utf8);}catch{}}
   throw;
  } finally {foreach(var stream in streams)stream.Dispose();}
 }
 internal static string Archive(string savePath,string managedDir,string backupRoot,string configText=null) {
  string name=ManagedName(savePath,managedDir);string[] files=Files(managedDir,name);var streams=LockFiles(files,true);string archive="";var moved=new List<string>();
  try {
   archive=NewFolder(backupRoot,"deleted");File.WriteAllText(Path.Combine(archive,"存档名称.txt"),name,Utf8);
   File.WriteAllText(Path.Combine(archive,"恢复说明.txt"),"被移除的战役："+name+"\r\n\r\n此目录保留删除前的世界、角色记录及现有两组历史备份。\r\n恢复前请保存并停止服务器。\r\n将本目录内 "+name+".sav、同名 .json 及其 .backup1/.backup2 文件一起复制到：\r\n"+Path.GetFullPath(managedDir)+"\r\n在助手中刷新列表，选择该存档再开服。\r\n不要覆盖其他同名世界；此目录中的服务器配置只用于核对删除前设置。\r\n",Utf8);
   if(configText!=null)File.WriteAllText(Path.Combine(archive,"删除前的服务器配置.json"),configText,Utf8);
   foreach(var file in files){string dest=Path.GetFullPath(Path.Combine(archive,Path.GetFileName(file)));RequireChild(file,managedDir);RequireChild(dest,archive);File.Move(file,dest);moved.Add(file);}
   return archive;
  } catch {
   foreach(var original in moved.AsEnumerable().Reverse()){string backup=Path.Combine(archive,Path.GetFileName(original));try{if(!File.Exists(original)&&File.Exists(backup))File.Move(backup,original);}catch{}}
   throw new Exception("存档移除未完成，已尝试恢复原文件。若仍有文件缺失，请从此目录恢复："+archive);
  } finally {foreach(var stream in streams)stream.Dispose();}
 }
 internal static void Restore(string archive,string managedDir) {
  string name=File.ReadAllText(Path.Combine(archive,"存档名称.txt"),Utf8);if(!CoopLauncher.ValidSaveName(name))throw new Exception("恢复备份名称无效。");
  var files=Files(archive,name);foreach(var file in files){string target=Path.GetFullPath(Path.Combine(managedDir,Path.GetFileName(file)));RequireChild(target,managedDir);if(File.Exists(target))throw new Exception("恢复目标已有同名文件，原备份仍保留在："+archive);}
  Directory.CreateDirectory(managedDir);
  try{foreach(var file in files){string target=Path.GetFullPath(Path.Combine(managedDir,Path.GetFileName(file)));RequireChild(file,archive);RequireChild(target,managedDir);File.Copy(file,target,false);}}
  catch(Exception error){throw new Exception("恢复尚未完成，完整备份仍保留在："+archive+"\r\n"+error.Message);}
 }
 internal static bool HasFiles(string managedDir,string name) {
  if(!CoopLauncher.ValidSaveName(name))throw new Exception("存档名称无效。");
  return Files(managedDir,name).Length!=0;
 }
 internal static void ValidateRename(string oldName,string newName,string managedDir) {
  if(!CoopLauncher.ValidSaveName(oldName)||!CoopLauncher.ValidSaveName(newName)||newName.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)||newName.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new Exception("请填写普通存档名称，不要包含路径、.sav、.json 或保留的内部名称。");
  if(string.Equals(oldName,newName,StringComparison.OrdinalIgnoreCase))throw new Exception("新名称与原名称相同。若只修改英文字母大小写，请先改成另一个临时名称，再改成目标名称。");
  foreach(var extension in Extensions) {
   string target=Path.GetFullPath(Path.Combine(managedDir,newName+extension));RequireChild(target,managedDir);
   if(File.Exists(target)||Directory.Exists(target))throw new Exception("“"+newName+"”已被存档、角色记录或历史备份使用，请换一个名称。");
  }
 }
 static string ConfigWithRenamedSave(string original,string oldName,string newName) {
  Jsonc.Parse(original);string configured=Jsonc.Scalar(original,"saveName") as string;
  return string.Equals(configured,oldName,StringComparison.OrdinalIgnoreCase)?Jsonc.Set(original,"saveName",newName):original;
 }
 static void AtomicText(string path,string text) {
  string temp=path+".save-management-"+Guid.NewGuid().ToString("N")+".tmp";
  try {File.WriteAllText(temp,text,Utf8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  finally {if(File.Exists(temp))File.Delete(temp);}
 }
 static void AtomicBytes(string path,byte[] contents) {
  string temp=path+".save-management-"+Guid.NewGuid().ToString("N")+".tmp";
  try {File.WriteAllBytes(temp,contents);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  finally {if(File.Exists(temp))File.Delete(temp);}
 }
 internal static void Rename(string savePath,string managedDir,string newName,string configPath,Action<string,string> writeConfig=null) {
  string name=ManagedName(savePath,managedDir);ValidateRename(name,newName,managedDir);
  string original=File.ReadAllText(configPath),replacement=ConfigWithRenamedSave(original,name,newName);byte[] originalBytes=File.ReadAllBytes(configPath);
  string[] files=Files(managedDir,name);var streams=LockFiles(files,true);var moved=new List<KeyValuePair<string,string>>();bool configAttempted=false;
  try {
   foreach(var source in files) {
    string target=Path.GetFullPath(Path.Combine(managedDir,newName+Path.GetFileName(source).Substring(name.Length)));RequireChild(source,managedDir);RequireChild(target,managedDir);
    File.Move(source,target);moved.Add(new KeyValuePair<string,string>(source,target));
   }
   if(original!=replacement) {
    if(!File.ReadAllBytes(configPath).SequenceEqual(originalBytes))throw new Exception("服务器设置刚刚被其他程序更改，请刷新后重试。");
    configAttempted=true;(writeConfig??AtomicText)(configPath,replacement);
   }
  } catch(Exception error) {
   var failures=new List<string>();
   foreach(var pair in moved.AsEnumerable().Reverse())try{if(!File.Exists(pair.Key)&&File.Exists(pair.Value))File.Move(pair.Value,pair.Key);else failures.Add(Path.GetFileName(pair.Key));}catch{failures.Add(Path.GetFileName(pair.Key));}
   if(configAttempted)try{AtomicBytes(configPath,originalBytes);}catch{failures.Add("服务器设置");}
   if(failures.Count!=0)throw new Exception("存档改名未完成，部分内容需要核对："+string.Join("、",failures.ToArray())+"。\r\n请在服务器存档目录核对“"+name+"”与“"+newName+"”。\r\n"+error.Message);
   throw new Exception("存档改名未完成，原存档与原设置已保留。\r\n"+error.Message);
  } finally {foreach(var stream in streams)stream.Dispose();}
 }
 internal static void RenamePending(string oldName,string newName,string managedDir,string configPath) {
  ValidateRename(oldName,newName,managedDir);if(HasFiles(managedDir,oldName))throw new Exception("服务器中已出现该存档文件，请刷新后再改名。");
  string original=File.ReadAllText(configPath),replacement=ConfigWithRenamedSave(original,oldName,newName);if(original!=replacement)AtomicText(configPath,replacement);
 }
 static void RecycleFolder(string folder) {
  string root=Path.GetPathRoot(Path.GetFullPath(folder));
  if(string.IsNullOrEmpty(root)||root.StartsWith("\\\\",StringComparison.Ordinal)||new DriveInfo(root).DriveType!=DriveType.Fixed)throw new Exception("当前存档位置不支持安全移入 Windows 回收站，请将服务器数据放到本机固定磁盘后重试。");
  Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
 }
 internal static void DeleteToRecycle(string savePath,string managedDir,string archiveRoot,string configPath,string nextName,Action<string> recycleAction=null,Action<string,string> writeConfig=null) {
  string name=ManagedName(savePath,managedDir);if(!string.IsNullOrEmpty(nextName)&&(!CoopLauncher.ValidSaveName(nextName)||string.Equals(name,nextName,StringComparison.OrdinalIgnoreCase)))throw new Exception("删除后的开服存档名称无效。");
  string original=File.ReadAllText(configPath),replacement=ConfigWithRenamedSave(original,name,nextName??"");byte[] originalBytes=File.ReadAllBytes(configPath);string[] files=Files(managedDir,name);
  string archive=Archive(savePath,managedDir,archiveRoot,original);bool configAttempted=false;
  try {
   if(original!=replacement) {
    if(!File.ReadAllBytes(configPath).SequenceEqual(originalBytes))throw new Exception("服务器设置刚刚被其他程序更改，请刷新后重试。");
    configAttempted=true;(writeConfig??AtomicText)(configPath,replacement);
   }
   (recycleAction??RecycleFolder)(archive);
   if(Directory.Exists(archive))throw new Exception("回收站操作未移走存档目录。");
  } catch(Exception error) {
   var failures=new List<string>();
   foreach(var source in files) {
    string backup=Path.Combine(archive,Path.GetFileName(source));
    try{if(!File.Exists(source)&&File.Exists(backup))File.Move(backup,source);else failures.Add(Path.GetFileName(source));}catch{failures.Add(Path.GetFileName(source));}
   }
   if(failures.Count==0&&configAttempted)try{AtomicBytes(configPath,originalBytes);}catch{failures.Add("服务器设置");}
   if(failures.Count!=0)throw new Exception("存档已移出服务器目录，但回收站处理未确认。请先核对 Windows 回收站与此恢复目录：\r\n"+archive+"\r\n尚需核对："+string.Join("、",failures.ToArray())+"\r\n"+error.Message);
   throw new Exception("删除未完成，已恢复原存档与原设置。\r\n"+error.Message);
  }
 }
 internal static void Check(string reportPath,List<string> results) {
  string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"save-tests-"+Guid.NewGuid().ToString("N")));RequireChild(root,Path.GetDirectoryName(Path.GetFullPath(reportPath)));
  string saves=Path.Combine(root,"saves"),desktop=Path.Combine(root,"desktop"),archiveRoot=Path.Combine(root,"archives");Directory.CreateDirectory(saves);Directory.CreateDirectory(desktop);
  string name="测试战役";foreach(var ext in Extensions)File.WriteAllText(Path.Combine(saves,name+ext),"fixture-"+ext,Utf8);
  File.WriteAllText(Path.Combine(saves,name+"10.sav"),"neighbour",Utf8);File.WriteAllText(Path.Combine(saves,"default_new_game.sav"),"template",Utf8);File.WriteAllText(Path.Combine(saves,"TransferSave.json"),"transfer",Utf8);
  string main=Path.Combine(saves,name+".sav");var original=Files(saves,name).ToDictionary(Path.GetFileName,File.ReadAllBytes);
  string export=Export(main,saves,desktop),second=Export(main,saves,desktop);
  if(export==second)throw new Exception("Export folder overwritten");foreach(var item in original){if(!File.ReadAllBytes(Path.Combine(export,item.Key)).SequenceEqual(item.Value)||!File.ReadAllBytes(Path.Combine(second,item.Key)).SequenceEqual(item.Value))throw new Exception("Export file mismatch");}results.Add("PASS 桌面导出包含世界、角色记录及两组备份；多次导出不覆盖");
  string archive=Archive(main,saves,archiveRoot,"{\"saveName\":\"测试战役\"}");foreach(var item in original){if(File.Exists(Path.Combine(saves,item.Key))||!File.ReadAllBytes(Path.Combine(archive,item.Key)).SequenceEqual(item.Value))throw new Exception("Archived save mismatch");}
  if(!File.Exists(Path.Combine(saves,name+"10.sav"))||!File.Exists(Path.Combine(saves,"default_new_game.sav"))||!File.Exists(Path.Combine(saves,"TransferSave.json")))throw new Exception("Unrelated file moved");Restore(archive,saves);foreach(var item in original)if(!File.ReadAllBytes(Path.Combine(saves,item.Key)).SequenceEqual(item.Value)||!File.ReadAllBytes(Path.Combine(archive,item.Key)).SequenceEqual(item.Value))throw new Exception("Restore mismatch");results.Add("PASS 删除归档可恢复且恢复后保留备份，模板、传输文件和相似存档名保留");
  bool blocked=false;try{Archive(Path.Combine(saves,"default_new_game.sav"),saves,archiveRoot);}catch{blocked=true;}if(!blocked)throw new Exception("Template removal permitted");
  blocked=false;try{Archive(Path.Combine(export,name+".sav"),saves,archiveRoot);}catch{blocked=true;}if(!blocked)throw new Exception("Outside save removal permitted");results.Add("PASS 内部模板和目录外原始存档禁止删除");
  foreach(var active in new[]{name,""}){blocked=false;try{CoopLauncher.RequireInactiveSave(name,true,active);}catch{blocked=true;}if(!blocked)throw new Exception("Active or unknown world deletion permitted");}CoopLauncher.RequireInactiveSave(name,true,"another-world");CoopLauncher.RequireInactiveSave(name,false,"");results.Add("PASS 使用中或运行状态未知的存档禁止删除");
  CheckManagement(saves,archiveRoot,root,results);
 }
 static void CheckManagement(string saves,string archiveRoot,string root,List<string> results) {
  string name="测试战役",renamed="改名后的战役",main=Path.Combine(saves,name+".sav"),config=Path.Combine(root,"server-config.json");
  string original="{ // keep this comment\r\n \"saveName\": \"测试战役\", \"password\": \"private-fixture\", \"autosaveMinutes\": 7, \"difficulty\": { \"combatAIDifficulty\": \"Realistic\" }, \"unknownOption\": 42, }\r\n";
  File.WriteAllText(config,original,new UTF8Encoding(true));var contents=Files(saves,name).ToDictionary(f=>Path.GetFileName(f).Substring(name.Length),File.ReadAllBytes);
  Rename(main,saves,renamed,config);
  foreach(var item in contents)if(File.Exists(Path.Combine(saves,name+item.Key))||!File.ReadAllBytes(Path.Combine(saves,renamed+item.Key)).SequenceEqual(item.Value))throw new Exception("Rename changed or omitted paired save contents");
  if(File.ReadAllText(config)!=Jsonc.Set(original,"saveName",renamed))throw new Exception("Rename changed password, difficulty, comment or unknown config field");
  string unmatched=Jsonc.Set(original,"saveName","other-campaign");File.WriteAllText(config,unmatched,new UTF8Encoding(true));byte[] unmatchedBytes=File.ReadAllBytes(config);
  Rename(Path.Combine(saves,renamed+".sav"),saves,name,config);if(!File.ReadAllBytes(config).SequenceEqual(unmatchedBytes))throw new Exception("Unselected save rename changed server config");
  results.Add("PASS 改名同步世界、角色与两组备份且内容不变；只更新匹配的 saveName，密码、难度、注释及其他配置保留");
  bool blocked=false;foreach(var invalid in new[]{"../outside","bad/name","default_new_game","TransferSave","name.backup2","new.sav","new.json",name}) {
   blocked=false;try{Rename(main,saves,invalid,config);}catch{blocked=true;}if(!blocked)throw new Exception("Unsafe or unchanged rename permitted "+invalid);
  }
  File.WriteAllText(Path.Combine(saves,"occupied.backup2.json"),"collision",Utf8);blocked=false;try{Rename(main,saves,"occupied",config);}catch{blocked=true;}if(!blocked||!File.Exists(main)||File.ReadAllText(Path.Combine(saves,"occupied.backup2.json"))!="collision")throw new Exception("Orphan backup rename collision was overwritten");
  File.WriteAllText(Path.Combine(saves,"CaseName.sav"),"case",Utf8);blocked=false;try{Rename(Path.Combine(saves,"CaseName.sav"),saves,"casename",config);}catch{blocked=true;}if(!blocked||!File.Exists(Path.Combine(saves,"CaseName.sav")))throw new Exception("Case-only rename silently modified files");
  using(var fileLock=new FileStream(Path.Combine(saves,name+".backup2.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.Read)) {
   blocked=false;try{Rename(main,saves,"locked-rename",config);}catch{blocked=true;}if(!blocked||File.Exists(Path.Combine(saves,"locked-rename.sav")))throw new Exception("Locked save rename was permitted");
  }
  File.WriteAllText(config,original,new UTF8Encoding(true));byte[] originalBytes=File.ReadAllBytes(config);
  blocked=false;try{Rename(main,saves,"rolled-back-rename",config,(path,text)=>{File.WriteAllText(path,text,Utf8);throw new IOException("fixture config write failure");});}catch{blocked=true;}
  if(!blocked||!File.ReadAllBytes(config).SequenceEqual(originalBytes))throw new Exception("Rename config rollback failed");foreach(var item in contents)if(!File.ReadAllBytes(Path.Combine(saves,name+item.Key)).SequenceEqual(item.Value)||File.Exists(Path.Combine(saves,"rolled-back-rename"+item.Key)))throw new Exception("Rename file rollback failed");
  results.Add("PASS 改名拒绝路径、内部名、重名、孤立备份冲突及占用文件；配置写入失败完整回滚，大小写改名有明确提示");
  RenamePending("待创建战役","待创建的新名字",saves,config);if(HasFiles(saves,"待创建战役")||HasFiles(saves,"待创建的新名字")||!File.ReadAllBytes(config).SequenceEqual(originalBytes))throw new Exception("Pending save rename created data or changed unrelated configuration");
  blocked=false;try{DeleteToRecycle(main,saves,archiveRoot,config,name+"10",folder=>{throw new IOException("fixture recycle unavailable");});}catch{blocked=true;}
  if(!blocked||!File.ReadAllBytes(config).SequenceEqual(originalBytes))throw new Exception("Recycle failure config rollback failed");foreach(var item in contents)if(!File.ReadAllBytes(Path.Combine(saves,name+item.Key)).SequenceEqual(item.Value))throw new Exception("Recycle failure file rollback failed");
  blocked=false;bool recycled=false;try{DeleteToRecycle(main,saves,archiveRoot,config,name+"10",folder=>{recycled=true;},(path,text)=>{File.WriteAllText(path,text,Utf8);throw new IOException("fixture deletion config failure");});}catch{blocked=true;}
  if(!blocked||recycled||!File.ReadAllBytes(config).SequenceEqual(originalBytes))throw new Exception("Deletion config failure reached recycle or failed rollback");foreach(var item in contents)if(!File.ReadAllBytes(Path.Combine(saves,name+item.Key)).SequenceEqual(item.Value))throw new Exception("Deletion config failure file rollback failed");
  string fakeRecycle=Path.Combine(root,"simulated-recycle-folder");DeleteToRecycle(main,saves,archiveRoot,config,name+"10",folder=>Directory.Move(folder,fakeRecycle));
  foreach(var item in contents)if(File.Exists(Path.Combine(saves,name+item.Key))||!File.ReadAllBytes(Path.Combine(fakeRecycle,name+item.Key)).SequenceEqual(item.Value))throw new Exception("Deleted files remained in server folder or grouped recycle contents were incomplete");
  if(File.ReadAllText(config)!=Jsonc.Set(original,"saveName",name+"10")||!File.Exists(Path.Combine(saves,name+"10.sav"))||!File.Exists(Path.Combine(saves,"default_new_game.sav"))||!File.Exists(Path.Combine(saves,"TransferSave.json")))throw new Exception("Deletion changed unrelated saves or config");
  results.Add("PASS 删除移出真实服务器目录并整组回收；回收或配置失败恢复原件，其他存档与内部文件保留；测试只使用独立夹具");
 }
}
