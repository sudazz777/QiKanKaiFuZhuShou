using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class CoopLauncher {
 void RequireSaveManagementIdle() {
  if(pendingExport!=null||pendingRestart!=null||pendingInstantSave!=null)throw new Exception("请等待当前保存任务结束，再修改存档。");
 }
 void RequireSelectedSaveInactive(string name) {
  RefreshStatus();using(var process=FindServer()){RequireInactiveSave(name,process!=null||PortInUse(),runningSave);}
 }
 void RenameSelectedSave() {
  RequireSaveManagementIdle();var choice=SelectedSave();RequireSelectedSaveInactive(choice.Name);
  string path=ManagedSavePath(choice,false),oldName=choice.Name;bool exists=File.Exists(path);
  if(!exists&&(choice.Source!=null||SaveStorage.HasFiles(SavesDir,oldName)))throw new Exception("存档文件不完整，请在存档文件夹中核对世界与角色记录。");
  using(var dialog=new RenameSaveDialog(oldName,exists)) {
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;string newName=dialog.SaveName;
   SaveStorage.ValidateRename(oldName,newName,SavesDir);
   if(saves.Items.Cast<SaveChoice>().Any(x=>!object.ReferenceEquals(x,choice)&&string.Equals(x.Name,newName,StringComparison.OrdinalIgnoreCase)))throw new Exception("列表中已有同名存档或待创建的新档，请换一个名称。");
   RequireSaveManagementIdle();RequireSelectedSaveInactive(oldName);
   if(exists)SaveStorage.Rename(path,SavesDir,newName,ConfigPath);else SaveStorage.RenamePending(oldName,newName,SavesDir,ConfigPath);
   string configuredName=Jsonc.Scalar(File.ReadAllText(ConfigPath),"saveName") as string;autoStart=!string.IsNullOrEmpty(configuredName);
   RefreshSaves(newName);
   info.Text=exists?"已把服务器存档“"+oldName+"”改为“"+newName+"”。\r\n当前进度、联机角色与现有历史备份已一起改名。":"待创建的新档已改名为“"+newName+"”，没有生成或更改战役进度。";
   LogEvent("存档改名 "+oldName+" → "+newName);
  }
 }
 void DeleteServerSave() {
  RequireSaveManagementIdle();var choice=SelectedSave();RequireSelectedSaveInactive(choice.Name);
  string path=ManagedSavePath(choice,false);bool exists=File.Exists(path);
  if(!exists) {
   if(choice.Source!=null||SaveStorage.HasFiles(SavesDir,choice.Name))throw new Exception("存档文件不完整，请在存档文件夹中核对世界与角色记录。");
   if(MessageBox.Show(this,"“"+choice.Name+"”尚未生成。移除这个待创建的新档选项吗？","移除新档选项",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
  } else {
   if(MessageBox.Show(this,"从这台电脑的服务器删除战役“"+choice.Name+"”？\r\n该世界、同名联机角色记录及两组历史备份会一起移入 Windows 回收站。\r\n删除后将从服务器存档列表移除；可从回收站还原整个存档文件夹。","删除服务器存档",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
  }
  RequireSaveManagementIdle();RequireSelectedSaveInactive(choice.Name);
  string original=File.ReadAllText(ConfigPath),configuredName=Jsonc.Scalar(original,"saveName") as string;
  string nextName=Directory.Exists(SavesDir)?Directory.GetFiles(SavesDir,"*.sav").Where(f=>new FileInfo(f).Length>0).Select(Path.GetFileNameWithoutExtension).Where(n=>ValidSaveName(n)&&!string.Equals(n,choice.Name,StringComparison.OrdinalIgnoreCase)).OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).FirstOrDefault():null;
  if(exists)SaveStorage.DeleteToRecycle(path,SavesDir,Path.Combine(DataDir,"存档删除暂存"),ConfigPath,nextName);
  else {
   if(SaveStorage.HasFiles(SavesDir,choice.Name))throw new Exception("服务器中刚刚出现该存档文件，请刷新后重试。");
   if(string.Equals(configuredName,choice.Name,StringComparison.OrdinalIgnoreCase))WriteAtomic(ConfigPath,Jsonc.Set(original,"saveName",nextName??""));
  }
  string remainingName=Jsonc.Scalar(File.ReadAllText(ConfigPath),"saveName") as string;autoStart=!string.IsNullOrEmpty(remainingName);
  RefreshSaves(remainingName);
  for(int i=saves.Items.Count-1;i>=0;i--)if(string.Equals(((SaveChoice)saves.Items[i]).Name,choice.Name,StringComparison.OrdinalIgnoreCase))saves.Items.RemoveAt(i);
  info.Text=exists?"已从服务器删除“"+choice.Name+"”，世界、角色记录与历史备份已一起移入 Windows 回收站。":"已移除尚未生成的新档“"+choice.Name+"”。";
  if(!autoStart)info.AppendText("\r\n请选择或新建存档，再启动服务器。");
  LogEvent("已从服务器删除/移除存档 "+choice.Name+(exists?"；已移入 Windows 回收站":""));
 }
}

internal sealed class RenameSaveDialog : Form {
 readonly TextBox name=new TextBox();public string SaveName{get{return name.Text;}}
 public RenameSaveDialog(string oldName,bool exists) {
  Text="修改服务器存档名称";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(480,207);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  var label=new Label{Text="新存档名称",Location=new Point(18,18),AutoSize=true};name.SetBounds(20,52,440,30);name.MaxLength=80;name.Text=oldName;
  var description=new Label{Text=exists?"世界、联机角色记录与现有历史备份会一起改名。\r\n请只填写名称；当前进度会保留。":"更改待创建的新档名称；不会生成战役。\r\n请只填写名称，不用加 .sav 或 .json。",Location=new Point(18,95),Size=new Size(443,47),ForeColor=Color.DimGray};
  var ok=new Button{Text="修改名称",DialogResult=DialogResult.OK};ok.SetBounds(243,159,106,34);var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel};cancel.SetBounds(361,159,99,34);
  Controls.AddRange(new Control[]{label,name,description,ok,cancel});AcceptButton=ok;CancelButton=cancel;Shown+=(s,e)=>{name.Focus();name.SelectAll();};
 }
}
