using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class CoopLauncher {
 readonly TabPage installationPage=new TabPage("本机安装设置");TabPage worldPage,difficultyPage;
 readonly TextBox installSteam=new TextBox(),installGame=new TextBox(),installServer=new TextBox(),installResult=new TextBox();
 readonly Label installData=new Label();readonly Button applyInstall=new Button();bool installationReady,firstSetup;
 void BuildInstallationPage() {
  installationPage.BackColor=Color.White;
  var intro=new Label{Text="首次开启会自动识别并适配本机安装位置。游戏移动或检测不准时，可在这里重新选择。",Location=new Point(18,14),Size=new Size(688,30),Font=new Font(Font.FontFamily,9),ForeColor=Color.DimGray};installationPage.Controls.Add(intro);
  BuildPathRow("Steam 程序",installSteam,49,()=>BrowseExecutable(installSteam,"选择本机 Steam.exe","Steam.exe","Steam 程序 (Steam.exe)|Steam.exe",false));
  BuildPathRow("游戏文件夹",installGame,90,BrowseGame);
  BuildPathRow("服务器文件夹",installServer,131,()=>BrowseExecutable(installServer,"选择 BannerlordCoopServer.exe","BannerlordCoopServer.exe","Coop 服务器 (BannerlordCoopServer.exe)|BannerlordCoopServer.exe",true));
  installData.SetBounds(18,174,682,40);installData.Font=new Font(Font.FontFamily,8);installData.ForeColor=Color.DimGray;
  installResult.Multiline=true;installResult.ReadOnly=true;installResult.ScrollBars=ScrollBars.Vertical;installResult.SetBounds(18,215,682,60);installResult.BackColor=Color.FromArgb(246,248,251);
  var detect=new Button{Text="重新自动检测"};detect.SetBounds(18,281,150,34);detect.Click+=(s,e)=>Guard(()=>{FillInstallation(HostInstallation.Detect());ShowInstallationResult();});
  var check=new Button{Text="检查安装"};check.SetBounds(182,281,117,34);check.Click+=(s,e)=>Guard(ShowInstallationResult);
  applyInstall.Text="保存并完成适配";applyInstall.SetBounds(464,281,236,34);applyInstall.Click+=(s,e)=>Guard(()=>ApplyInstallation(InstallationDraft(),true));applyInstall.Enabled=!preview;
  installationPage.Controls.AddRange(new Control[]{installData,installResult,detect,check,applyInstall});FillInstallation(HostInstallation.Current);ShowInstallationResult();
 }
 void BuildPathRow(string title,TextBox box,int y,Action browse) {
  installationPage.Controls.Add(new Label{Text=title,Location=new Point(18,y+5),Size=new Size(124,27)});box.SetBounds(144,y,455,30);installationPage.Controls.Add(box);
  var button=new Button{Text="选择…"};button.SetBounds(611,y-2,89,34);button.Click+=(s,e)=>Guard(()=>{browse();ShowInstallationResult();});installationPage.Controls.Add(button);
 }
 void BrowseExecutable(TextBox box,string title,string fileName,string filter,bool directory) {
  using(var dialog=new OpenFileDialog{Title=title,Filter=filter,FileName=fileName,CheckFileExists=true}) {
   string initial=directory?box.Text:Path.GetDirectoryName(box.Text);if(!string.IsNullOrWhiteSpace(initial)&&Directory.Exists(initial))dialog.InitialDirectory=initial;
   if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=directory?Path.GetDirectoryName(dialog.FileName):dialog.FileName;
  }
 }
 void BrowseGame(){using(var dialog=new FolderBrowserDialog{Description="选择含 bin、Modules 的《骑马与砍杀2》游戏文件夹",ShowNewFolderButton=false}){if(Directory.Exists(installGame.Text))dialog.SelectedPath=installGame.Text;if(dialog.ShowDialog(this)==DialogResult.OK)installGame.Text=dialog.SelectedPath;}}
 HostInstallation InstallationDraft(){return new HostInstallation{SteamExe=CleanPath(installSteam.Text),GameDir=CleanPath(installGame.Text),ServerDir=CleanPath(installServer.Text)};}
 static string CleanPath(string value){return string.IsNullOrWhiteSpace(value)?"":Path.GetFullPath(value.Trim().Trim('"'));}
 void FillInstallation(HostInstallation install){installSteam.Text=install.SteamExe;installGame.Text=install.GameDir;installServer.Text=install.ServerDir;installData.Text="本机存档目录（按 Windows 文档位置自动适配）：\r\n"+install.DataDir;}
 void ShowInstallationResult(){var draft=InstallationDraft();string[] errors=HostBootstrap.Inspect(draft);installResult.Text=errors.Length==0?"安装与版本检查通过。适配将保留当前存档、密码与难度；首次开服请按服务器提示完成协议确认。":string.Join("\r\n",errors);installResult.ForeColor=errors.Length==0?Color.DarkGreen:Color.Firebrick;}
 void SetInstallationControls(){worldPage.Enabled=installationReady;difficultyPage.Enabled=installationReady;}
 void OnLauncherShown() {
  Rectangle available=Screen.FromControl(this).WorkingArea;int fitWidth=Math.Max(MinimumSize.Width,available.Width-24),fitHeight=Math.Max(MinimumSize.Height,available.Height-24);if(Width>fitWidth||Height>fitHeight)Size=new Size(Math.Min(Width,fitWidth),Math.Min(Height,fitHeight));
  if(preview){info.Text="界面预览：不启动服务器、不修改设置。";RefreshStatus();return;}
  if(firstSetup||!settingsLoaded) {
   if(HostBootstrap.Inspect(HostInstallation.Current).Length==0)Guard(()=>ApplyInstallation(HostInstallation.Current,false));
   else {installationReady=false;settingsLoaded=false;SetInstallationControls();ShowInstallationResult();info.Text="首次检测未完成，请在“本机安装设置”中补全安装位置，再点“保存并完成适配”。";RefreshStatus();}
   return;
  }
  if(settingsLoaded&&autoStart&&saves.SelectedItem!=null)Guard(()=>StartServer(CaptureDraft()));else if(settingsLoaded)info.Text="请选择已有存档或新建存档，然后点击“启动服务器”。";
 }
 internal void RenderInstallation(string path){Opacity=0;Show();Application.DoEvents();tabs.SelectedTab=installationPage;info.Text="预览：首次自动检测，保存适配后可开服；此预览没有写入用户配置。";RefreshStatus();PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path,ImageFormat.Png);}Hide();}
 internal void RenderWorld(string path){Opacity=0;Show();Application.DoEvents();tabs.SelectedTab=worldPage;info.Text="预览：立刻保存当前运行战役；服务器存档支持改名、导出和删除。";RefreshStatus();PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path,ImageFormat.Png);}Hide();}
 internal void RenderDifficulty(string path){Opacity=0;Show();Application.DoEvents();tabs.SelectedTab=difficultyPage;info.Text="预览：按游戏中的名称和预设组合显示难度；此预览没有写入用户配置。";RefreshStatus();PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));using(var graphics=Graphics.FromImage(bitmap)){foreach(var combo in difficulty.Values.Concat(new[]{difficultyPreset})){if(combo.SelectedIndex<0)continue;Point origin=combo.PointToScreen(new Point(2,2));var bounds=new Rectangle(origin.X-Left,origin.Y-Top,combo.ClientSize.Width-23,combo.ClientSize.Height-4);using(var background=new SolidBrush(combo.Enabled?combo.BackColor:SystemColors.Control))graphics.FillRectangle(background,bounds);TextRenderer.DrawText(graphics,combo.GetItemText(combo.SelectedItem),combo.Font,bounds,combo.Enabled?combo.ForeColor:SystemColors.GrayText,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}}bitmap.Save(path,ImageFormat.Png);}Hide();}
 void ApplyInstallation(HostInstallation install,bool explicitlySelected) {
  if(preview)throw new Exception("预览模式不会修改安装设置。");
  if(pendingRestart!=null||pendingExport!=null||pendingInstantSave!=null)throw new Exception("请等待存档保存任务结束后再更改安装位置。");
  bool attachExisting=false;
  using(var running=FindServer()) {
   bool same=string.Equals(install.SteamExe,HostInstallation.Current.SteamExe,StringComparison.OrdinalIgnoreCase)&&string.Equals(install.GameDir,HostInstallation.Current.GameDir,StringComparison.OrdinalIgnoreCase)&&string.Equals(install.ServerDir,HostInstallation.Current.ServerDir,StringComparison.OrdinalIgnoreCase);
   if(running!=null||PortInUse()) {
    if(running!=null&&same&&File.Exists(install.ConfigPath)&&File.Exists(install.ModConfigPath)&&File.Exists(Path.Combine(install.SavesDir,"default_new_game.sav")))attachExisting=true;
    else throw new Exception("服务器仍在运行，请在原助手或服务器窗口保存并停止后，再更改本机安装设置。");
   }
  }
  if(attachExisting){string[] errors=HostBootstrap.Inspect(install);if(errors.Length!=0)throw new Exception(string.Join("\r\n",errors));}else HostBootstrap.Prepare(install);
  install.Save();
  bridge.Dispose();HostInstallation.Current=install;bridge=new SteamJoinBridge(install.ModuleBin);currentLog="";candidateLobby="";trackedPid=0;serverStarted=DateTime.MinValue;runningSave="";runningHasPassword=null;firstSetup=false;installationReady=true;
  try{LoadSettings();}catch{installationReady=false;SetInstallationControls();throw;}
  FillInstallation(install);ShowInstallationResult();SetInstallationControls();tabs.SelectedTab=worldPage;
  info.Text=(explicitlySelected?"本机安装设置已保存，适配完成。":"已自动识别本机游戏与服务器，适配完成。")+"\r\n"+(saves.Items.Count==0?"请点“新建存档”或“导入存档”，再启动服务器。":"已有存档与设置已保留。确认所选存档后点“启动服务器”。");RefreshStatus();
 }
}
