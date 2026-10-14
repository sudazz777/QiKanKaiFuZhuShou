using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyFileVersion("1.6.6.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("2026.10.14 关闭同步停服版")]

internal static class Program {
 [STAThread] static void Main(string[] args) {
  if((args.Length==1||args.Length==2) && args[0]=="--console-fixture") {ServerConsoleChecks.RunFixture(args.Length==2?args[1]:"");return;}
  if(args.Length==2 && args[0]=="--self-test") { HostInstallation.Current=HostInstallation.Detect();LauncherChecks.Run(args[1]); return; }
  if(args.Length==2 && args[0]=="--render-difficulty") {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);HostInstallation.Current=HostInstallation.Detect();using(var launcher=new CoopLauncher(true,false))launcher.RenderDifficulty(args[1]);return;}
  if(args.Length==2 && args[0]=="--render-world") {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);HostInstallation.Current=HostInstallation.Detect();using(var launcher=new CoopLauncher(true,false))launcher.RenderWorld(args[1]);return;}
  if(args.Length==2 && (args[0]=="--render-installation"||args[0]=="--render-empty-installation")) {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);HostInstallation.Current=args[0]=="--render-empty-installation"?new HostInstallation():HostInstallation.Detect();using(var launcher=new CoopLauncher(true,true))launcher.RenderInstallation(args[1]);return;}
  bool first;
  using(var mutex=new Mutex(true,"Local\\BannerlordCoopLauncher-Mason",out first)) {
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   if(!first){MessageBox.Show("开服助手已经打开，请使用现有窗口。","骑砍2开服助手");return;}
   HostInstallation installation;bool restored=HostInstallation.TryLoad(out installation);HostInstallation.Current=restored?installation:HostInstallation.Detect();
   Application.Run(new CoopLauncher(args.Contains("--preview"),!restored));
  }
 }
}

internal sealed partial class CoopLauncher : Form {
 internal static string ServerDir {get{return HostInstallation.Current.ServerDir;}}
 internal static string DataDir {get{return HostInstallation.Current.DataDir;}}
 internal static string GameUserDir {get{return HostInstallation.Current.GameUserDir;}}
 static string SteamExe {get{return HostInstallation.Current.SteamExe;}}
 internal static string ConfigPath {get{return HostInstallation.Current.ConfigPath;}}
 internal static string ModConfigPath {get{return HostInstallation.Current.ModConfigPath;}}
 internal static string SavesDir {get{return HostInstallation.Current.SavesDir;}}
 internal static readonly string[] DifficultyKeys={"playerReceivedDamage","playerTroopsReceivedDamage","combatAIDifficulty","recruitmentDifficulty","playerMapMovementSpeed","stealthAndDisguiseDifficulty","persuasionSuccessChance","clanMemberDeathChance","battleDeath"};
 static readonly string[] DifficultyNames={"玩家受到伤害","友方士兵承受伤害","战斗AI难度","招募难度","地图移动速度","潜行与伪装难度","说服难度","家族成员战死几率","英雄战死几率"};
 static readonly string[] Levels={"VeryEasy","Easy","Realistic"};
 static readonly string[] LevelNames={"非常简单","简单","写实"};
 string serverExe {get{return Path.Combine(ServerDir,"BannerlordCoopServer.exe");}}
 readonly Label localState=new Label(),steamState=new Label(),friendState=new Label(),notice=new Label();
 readonly TextBox info=new TextBox();
 readonly TextBox password=new TextBox();
 readonly CheckBox showPassword=new CheckBox();
 readonly Button start=new Button(),saveNow=new Button(),restart=new Button(),stop=new Button(),exportSave=new Button(),deleteSave=new Button(),renameSave=new Button();
 readonly ComboBox saves=new ComboBox();
 readonly NumericUpDown autosave=new NumericUpDown();
 readonly Dictionary<string,ComboBox> difficulty=new Dictionary<string,ComboBox>();
 readonly ComboBox difficultyPreset=new ComboBox();bool updatingDifficultyControls;
 readonly CheckBox birth=new CheckBox(),perks=new CheckBox();
 readonly TabControl tabs=new TabControl();
 readonly System.Windows.Forms.Timer statusTimer=new System.Windows.Forms.Timer(),steamTimer=new System.Windows.Forms.Timer();
 SteamJoinBridge bridge;
 string currentLog="",candidateLobby="",runningSave=""; DateTime serverStarted=DateTime.MinValue,restartDeadline=DateTime.MinValue;
 int trackedPid; bool ready,preview,settingsLoaded,autoStart; bool? runningHasPassword; SettingsDraft pendingRestart; PendingSaveExport pendingExport;
 public CoopLauncher(bool previewOnly,bool needsSetup) {
  firstSetup=needsSetup;installationReady=!needsSetup&&HostBootstrap.Inspect(HostInstallation.Current).Length==0;
  bridge=new SteamJoinBridge(HostInstallation.Current.ModuleBin);
  preview=previewOnly; Text="骑砍2开服助手 · 公开大厅与战役设置 · v1.6.6";
  LoadAppIcon();
  AutoScaleMode=AutoScaleMode.Dpi; Font=new Font("Microsoft YaHei UI",10);
  ClientSize=new Size(780,756);MinimumSize=new Size(600,450);AutoScroll=true;AutoScrollMinSize=new Size(780,756);FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;
  StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(246,248,251);
  var title=new Label{Text="骑砍2开服助手",Location=new Point(24,18),AutoSize=true,Font=new Font(Font.FontFamily,19,FontStyle.Bold),ForeColor=Color.FromArgb(28,42,62)};
  var badge=new Label{Text="公开大厅 · 密码可选",Location=new Point(500,25),Size=new Size(250,30),TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.FromArgb(30,96,156)};
  localState.SetBounds(26,67,728,26); steamState.SetBounds(26,96,728,26); friendState.SetBounds(26,125,728,28);
  notice.SetBounds(26,159,728,26); notice.ForeColor=Color.DimGray;
  tabs.SetBounds(24,199,732,357);
  worldPage=new TabPage("开服与存档"); difficultyPage=new TabPage("详细难度"); tabs.TabPages.AddRange(new[]{worldPage,difficultyPage,installationPage});
  BuildWorldPage(worldPage); BuildDifficultyPage(difficultyPage);BuildInstallationPage();BuildConsolePage();
  start.Text="启动服务器"; start.SetBounds(24,574,138,43); start.Click+=(s,e)=>Guard(()=>StartServer(CaptureDraft()));
  saveNow.Text="立刻保存"; saveNow.SetBounds(173,574,138,43); saveNow.Click+=(s,e)=>Guard(BeginInstantSave);
  restart.Text="保存并重启"; restart.SetBounds(322,574,138,43); restart.Click+=(s,e)=>Guard(BeginRestart);
  var game=new Button{Text="打开游戏"}; game.SetBounds(471,574,138,43); game.Click+=(s,e)=>Guard(OpenGame);
  stop.Text="保存并停止"; stop.SetBounds(620,574,136,43); stop.Click+=(s,e)=>Guard(StopServer);
  info.Multiline=true; info.ReadOnly=true; info.ScrollBars=ScrollBars.Vertical; info.SetBounds(24,632,732,72); info.BackColor=Color.White;
  var footer=new Label{Text="关闭助手会先保存并停止服务器；请等待保存完成，窗口将自动关闭。",Location=new Point(25,720),Size=new Size(733,24),ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)};
  Controls.AddRange(new Control[]{title,badge,localState,steamState,friendState,notice,tabs,start,saveNow,restart,game,stop,info,footer});
  if(installationReady)try{LoadSettings();}catch(Exception e){settingsLoaded=false;info.Text="配置读取失败："+e.Message;}
  SetInstallationControls();
  statusTimer.Interval=1000; statusTimer.Tick+=(s,e)=>RefreshStatus(); statusTimer.Start();
  steamTimer.Interval=100; steamTimer.Tick+=(s,e)=>{if(!preview&&installationReady)bridge.Tick(candidateLobby);friendState.Text="好友头像 / 资料页加入："+(!installationReady?"请先完成本机安装适配":preview?"预览模式，未发布加入入口":bridge.StatusText.Replace("Steam 好友加入入口：", ""));friendState.ForeColor=bridge.EntryReady?Color.DarkGreen:Color.DarkGoldenrod;};steamTimer.Start();
  Shown+=(s,e)=>OnLauncherShown();
  FormClosing+=RequestLauncherClose;
  FormClosed+=(s,e)=>{statusTimer.Dispose();steamTimer.Dispose();bridge.Dispose();DisposeShutdown();};
 }
 void LoadAppIcon() {
  try {
   using(var stream=typeof(CoopLauncher).Assembly.GetManifestResourceStream("CoopLauncher.AppIcon.ico")) {
    if(stream==null)return;
    using(var embeddedIcon=new Icon(stream))Icon=(Icon)embeddedIcon.Clone();
   }
  } catch { /* Keep the default window icon if the embedded resource is unavailable. */ }
 }
 void BuildWorldPage(TabPage page) {
  page.BackColor=Color.White;
  page.Controls.Add(new Label{Text="开服存档",Location=new Point(18,20),AutoSize=true});
  saves.DropDownStyle=ComboBoxStyle.DropDownList; saves.SetBounds(111,15,590,30); page.Controls.Add(saves);
  var refresh=new Button{Text="刷新存档"}; refresh.SetBounds(18,59,95,34); refresh.Click+=(s,e)=>Guard(()=>RefreshSaves(SelectedName()));
  var create=new Button{Text="新建存档"}; create.SetBounds(122,59,95,34); create.Click+=(s,e)=>Guard(CreateSave);
  var import=new Button{Text="导入存档"}; import.SetBounds(226,59,95,34); import.Click+=(s,e)=>Guard(ImportSave);
  renameSave.Text="修改存档名";renameSave.SetBounds(330,59,95,34);renameSave.Click+=(s,e)=>Guard(RenameSelectedSave);
  var folder=new Button{Text="存档文件夹"}; folder.SetBounds(434,59,118,34); folder.Click+=(s,e)=>Guard(()=>Process.Start(new ProcessStartInfo(SavesDir){UseShellExecute=true}));
  exportSave.Text="保存存档到桌面";exportSave.SetBounds(561,59,140,34);exportSave.Click+=(s,e)=>Guard(ExportSelectedSave);
  deleteSave.Text="删除服务器存档";deleteSave.ForeColor=Color.Firebrick;deleteSave.SetBounds(394,105,147,36);deleteSave.Click+=(s,e)=>Guard(DeleteServerSave);
  page.Controls.AddRange(new Control[]{refresh,create,import,folder,renameSave,exportSave,deleteSave});
  page.Controls.Add(new Label{Text="自动保存",Location=new Point(18,116),AutoSize=true});
  autosave.Minimum=0; autosave.Maximum=1440; autosave.SetBounds(112,110,100,30); page.Controls.Add(autosave);
  page.Controls.Add(new Label{Text="分钟（0 为关闭）",Location=new Point(221,116),AutoSize=true});
  var save=new Button{Text="保存设置"}; save.SetBounds(553,105,147,36); save.Click+=(s,e)=>Guard(()=>{var draft=CaptureDraft();ApplySettings(draft);info.Text="设置已保存并备份。正在运行的战役将在“保存并重启”后应用新设置。";});page.Controls.Add(save);
  page.Controls.Add(new Label{Text="房间密码",Location=new Point(18,158),AutoSize=true});
  password.SetBounds(112,151,327,30);password.MaxLength=128;password.UseSystemPasswordChar=true;page.Controls.Add(password);
  showPassword.Text="显示密码";showPassword.SetBounds(450,153,100,29);showPassword.CheckedChanged+=(s,e)=>password.UseSystemPasswordChar=!showPassword.Checked;page.Controls.Add(showPassword);
  var clearPassword=new Button{Text="清空密码"};clearPassword.SetBounds(553,148,147,36);clearPassword.Click+=(s,e)=>{password.Clear();showPassword.Checked=false;info.Text="密码输入已清空。点“保存并重启”后取消房间密码；服务器未运行时点“启动服务器”。";};page.Controls.Add(clearPassword);
  page.Controls.Add(new Label{Text="本机加入：Join Co-op Sandbox → Direct → 127.0.0.1，端口 4200。\r\n加入时填写服务器当前密码；未设置密码则留空。好友可在 Steam 资料页选择“加入游戏”。",Location=new Point(18,199),Size=new Size(685,59)});
  page.Controls.Add(new Label{Text="“立刻保存”保存当前运行战役，无需停服。改名或删除当前战役前请先保存并停止。\r\n列表显示服务器存档；单机存档请点“导入存档”。删除的服务器文件移入回收站。",Location=new Point(18,273),Size=new Size(685,44),ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)});
 }
 void BuildDifficultyPage(TabPage page) {
  page.BackColor=Color.White;
  page.Controls.Add(new Label{Text="总难度",Location=new Point(18,19),Size=new Size(143,25)});
  difficultyPreset.DropDownStyle=ComboBoxStyle.DropDownList;difficultyPreset.SetBounds(163,15,184,29);difficultyPreset.Items.AddRange(new[]{"冒险家","勇士","霸主","自定义"});page.Controls.Add(difficultyPreset);
  page.Controls.Add(new Label{Text="选择预设后，也可逐项调整。",Location=new Point(369,20),AutoSize=true,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)});
  for(int i=0;i<DifficultyKeys.Length;i++) {
   int col=i/5,row=i%5,x=18+col*351,y=58+row*37;
   page.Controls.Add(new Label{Text=DifficultyNames[i],Location=new Point(x,y+4),Size=new Size(143,25)});
   var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};combo.SetBounds(x+145,y,184,29);combo.Items.AddRange(DifficultyOptions(DifficultyKeys[i]));combo.SelectedIndex=0;combo.SelectedIndexChanged+=(s,e)=>{if(!updatingDifficultyControls)SyncDifficultyPreset();};difficulty.Add(DifficultyKeys[i],combo);page.Controls.Add(combo);
  }
  page.Controls.Add(new Label{Text="英雄战死需启用生育与死亡。",Location=new Point(369,212),AutoSize=true,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)});
  birth.Text="启用生育与死亡"; birth.SetBounds(18,250,250,27);birth.CheckedChanged+=(s,e)=>{if(!updatingDifficultyControls)SyncDifficultyPreset();};
  perks.Text="自动分配家族成员特长"; perks.SetBounds(369,250,290,27);page.Controls.AddRange(new Control[]{birth,perks});
  difficultyPreset.SelectedIndexChanged+=(s,e)=>{if(!updatingDifficultyControls&&difficultyPreset.SelectedIndex>=0&&difficultyPreset.SelectedIndex<3)ApplyDifficultyPreset(difficultyPreset.SelectedIndex);};
  SyncDifficultyPreset();
  page.Controls.Add(new Label{Text="更改后点“保存并重启”生效；逐项调整会显示为“自定义”。",Location=new Point(18,292),AutoSize=true,ForeColor=Color.DimGray,Font=new Font(Font.FontFamily,9)});
 }
 static string[] DifficultyOptions(string key) {
  if(key=="combatAIDifficulty"||key=="stealthAndDisguiseDifficulty")return new[]{"普通","资深","挑战"};
  if(key=="clanMemberDeathChance")return new[]{"禁用家族成员战死","-50%","写实"};
  if(key=="battleDeath")return new[]{"禁用所有英雄战死","禁用玩家英雄战死","启用所有英雄战死"};
  return LevelNames;
 }
 internal static int DifficultyPresetLevel(string key,int preset) {
  if(preset<0||preset>2||Array.IndexOf(DifficultyKeys,key)<0)throw new ArgumentException("无效的战役难度预设。");
  // The game's Bannerlord preset uses Veteran for stealth and disguise.
  return preset==2&&key=="stealthAndDisguiseDifficulty"?1:preset;
 }
 void ApplyDifficultyPreset(int preset) {
  updatingDifficultyControls=true;
  try{foreach(var key in DifficultyKeys){if(key=="battleDeath"&&!birth.Checked)continue;difficulty[key].SelectedIndex=DifficultyPresetLevel(key,preset);}}
  finally{updatingDifficultyControls=false;SyncDifficultyPreset();}
 }
 void SyncDifficultyPreset() {
  updatingDifficultyControls=true;
  try {
   difficulty["battleDeath"].Enabled=birth.Checked;int selected=3;
   for(int preset=0;preset<3;preset++){bool matches=true;foreach(var key in DifficultyKeys){if(key=="battleDeath"&&!birth.Checked)continue;if(difficulty[key].SelectedIndex!=DifficultyPresetLevel(key,preset)){matches=false;break;}}if(matches){selected=preset;break;}}
   difficultyPreset.SelectedIndex=selected;
  }finally{updatingDifficultyControls=false;}
 }
 void Guard(Action action) {try{action();}catch(Exception e){info.Text=e.Message;MessageBox.Show(e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
 string SelectedName(){var choice=saves.SelectedItem as SaveChoice;return choice==null?"":choice.Name;}
 void LoadSettings() {
  settingsLoaded=false;
  string config=File.ReadAllText(ConfigPath);string mod=File.ReadAllText(ModConfigPath);
  string configuredName=Jsonc.Scalar(config,"saveName") as string;RefreshSaves(configuredName);autoStart=!string.IsNullOrEmpty(configuredName);
  password.Text=Convert.ToString(Jsonc.Scalar(config,"password"));ValidatePassword(password.Text);
  autosave.Value=Math.Max(0,Math.Min(1440,Convert.ToInt32(Jsonc.Scalar(config,"autosaveMinutes"))));
  updatingDifficultyControls=true;
  try {
   foreach(var key in DifficultyKeys){string level=Jsonc.Scalar(mod,key) as string;int i=Array.IndexOf(Levels,level);if(i<0)throw new Exception("不支持的难度配置："+key);difficulty[key].SelectedIndex=i;}
   birth.Checked=Convert.ToBoolean(Jsonc.Scalar(mod,"birthAndDeath"));perks.Checked=Convert.ToBoolean(Jsonc.Scalar(mod,"autoAllocateClanMemberPerks"));
  }finally{updatingDifficultyControls=false;SyncDifficultyPreset();}
  settingsLoaded=true;
 }
 void RefreshSaves(string prefer) {
  var choices=new Dictionary<string,SaveChoice>(StringComparer.OrdinalIgnoreCase);
  foreach(var dir in new[]{SavesDir}) {
   if(!Directory.Exists(dir))continue;
   foreach(var file in Directory.GetFiles(dir,"*.sav")) {
    string name=Path.GetFileNameWithoutExtension(file);if(IsInternalSave(name)||new FileInfo(file).Length==0||!ValidSaveName(name))continue;
    if(!choices.ContainsKey(name))choices.Add(name,new SaveChoice{Name=name,Source=file});
   }
  }
  if(!string.IsNullOrEmpty(prefer)&&!choices.ContainsKey(prefer)&&ValidSaveName(prefer))choices.Add(prefer,new SaveChoice{Name=prefer});
  saves.Items.Clear();foreach(var item in choices.Values.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase))saves.Items.Add(item);
  for(int i=0;i<saves.Items.Count;i++)if(string.Equals(((SaveChoice)saves.Items[i]).Name,prefer,StringComparison.OrdinalIgnoreCase)){saves.SelectedIndex=i;break;}
  if(saves.SelectedIndex<0&&saves.Items.Count>0)saves.SelectedIndex=0;
 }
 static bool IsInternalSave(string name){return name.Equals("default_new_game",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(name,@"\.backup[12]$",RegexOptions.IgnoreCase)||name.Equals("TransferSave",StringComparison.OrdinalIgnoreCase);}
 internal static bool ValidSaveName(string name) {
  return !string.IsNullOrWhiteSpace(name)&&name.Length<=80&&name==name.Trim()&&!name.EndsWith(".")&&name.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!IsInternalSave(name)&&!Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase);
 }
 void CreateSave() {
  using(var dialog=new NewSaveDialog()) {
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;string name=dialog.SaveName;
   if(!ValidSaveName(name))throw new Exception("请使用普通存档名，不要包含路径、文件扩展名或特殊符号。");
   if(name.EndsWith(".sav",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new Exception("只填写存档名称，不用加 .sav 或 .json。");
   if(saves.Items.Cast<SaveChoice>().Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))||HasSaveFiles(name))throw new Exception("这个名称已被存档或角色记录使用，请换一个名称，原存档将保留。");
   if(!File.Exists(Path.Combine(SavesDir,"default_new_game.sav")))throw new Exception("服务器的初始世界模板缺失，暂时无法新建战役。");
   var choice=new SaveChoice{Name=name};saves.Items.Add(choice);saves.SelectedItem=choice;
   info.Text="已选新档“"+name+"”。点“启动服务器”或“保存并重启”后生成新战役。原有存档保留。";
  }
 }
 static bool HasSaveFiles(string name) {
  foreach(var dir in new[]{SavesDir,Path.Combine(GameUserDir,"Game Saves")})
   foreach(var ext in new[]{".sav",".json",".backup1.sav",".backup1.json",".backup2.sav",".backup2.json"})if(File.Exists(Path.Combine(dir,name+ext)))return true;
  return false;
 }
 void ImportSave() {
  using(var dialog=new OpenFileDialog{Title="选择要导入的战役存档（联机存档请保留同名 JSON）",Filter="战役存档 (*.sav)|*.sav",InitialDirectory=Path.Combine(GameUserDir,"Game Saves")}) {
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;string name=Path.GetFileNameWithoutExtension(dialog.FileName);
   if(!ValidSaveName(name))throw new Exception("此存档名称不可用于开服。");
   if(saves.Items.Cast<SaveChoice>().Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))||File.Exists(Path.Combine(SavesDir,name+".json")))throw new Exception("列表或服务器中已有同名记录，请选择现有存档；导入其他世界请先换一个存档名。");
   var item=new SaveChoice{Name=name,Source=dialog.FileName};saves.Items.Add(item);saves.SelectedItem=item;info.Text="已选存档“"+name+"”。保存设置时会一起复制存在的同名联机角色文件。";
  }
 }
 SaveChoice SelectedSave() {var choice=saves.SelectedItem as SaveChoice;if(choice==null||!ValidSaveName(choice.Name))throw new Exception("请先在存档列表中选择战役。");return choice;}
 string ManagedSavePath(SaveChoice choice,bool requireExists) {
  string path=Path.Combine(SavesDir,choice.Name+".sav");
  if(choice.Source!=null&&!string.Equals(Path.GetFullPath(choice.Source),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))throw new Exception("此存档尚未导入服务器。请先点“保存设置”完成导入，再刷新列表后管理服务器中的副本。");
  if(requireExists&&!File.Exists(path))throw new Exception("新档还没有生成。请先开服并保存战役，再导出或删除。");return path;
 }
 static bool PortInUse(){return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(x=>x.Port==4200);}
 void ExportSelectedSave() {
  if(pendingExport!=null||pendingRestart!=null||pendingInstantSave!=null)throw new Exception("请等待当前保存任务结束。");
  var choice=SelectedSave();RefreshStatus();
  using(var p=FindServer()) {
   bool running=p!=null||PortInUse();
   if(running&&string.IsNullOrEmpty(runningSave))throw new Exception("尚未确认正在运行的存档，请等服务器加载完成，或先保存并停止后导出。");
   bool active=running&&string.Equals(runningSave,choice.Name,StringComparison.OrdinalIgnoreCase);
   string path=ManagedSavePath(choice,!active);
   if(active) {
    if(p==null||!ready)throw new Exception("请等服务器就绪，或先手动保存并停止后导出。");
    var answer=MessageBox.Show(this,"当前战役“"+choice.Name+"”正在运行。\r\n导出前将保存战役并停止服务器，好友会断开连接。\r\n导出完成后，可点“启动服务器”重新开服。\r\n\r\n现在保存并导出到桌面吗？","保存并导出战役",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
    if(answer!=DialogResult.Yes)return;
    RefreshStatus();
    if(p.HasExited||trackedPid!=p.Id||!ready||!string.Equals(runningSave,choice.Name,StringComparison.OrdinalIgnoreCase)||string.IsNullOrEmpty(currentLog)||!File.Exists(currentLog))throw new Exception("服务器状态已改变，尚未请求停服。请刷新后重试导出。");
    var job=new PendingSaveExport{Name=choice.Name,Path=path,LogPath=currentLog,LogOffset=new FileInfo(currentLog).Length,ProcessId=p.Id,Deadline=DateTime.Now.AddSeconds(90)};
    ConsoleInput.Send((uint)p.Id,"stop");pendingExport=job;tabs.Enabled=false;
    info.Text="正在保存战役并停止服务器，退出后自动把完整存档导出到桌面。";LogEvent("请求保存并导出存档 "+choice.Name);RefreshStatus();return;
   }
   CompleteExport(choice.Name,path);
  }
 }
 void CompleteExport(string name,string path) {
  string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);if(string.IsNullOrEmpty(desktop)||!Directory.Exists(desktop))throw new Exception("未找到桌面文件夹，已取消导出。");
  string target=SaveStorage.Export(path,SavesDir,desktop);
  bool hasPlayers=File.Exists(Path.ChangeExtension(path,".json"));
  info.Text="已导出“"+name+"”到桌面：\r\n"+target+(hasPlayers?"":"\r\n此存档没有同名联机角色记录，已导出世界文件及现有备份。");LogEvent("已导出存档 "+name+" 至 "+target);
  MessageBox.Show(this,"存档已保存到桌面的独立文件夹：\r\n"+target+"\r\n\r\n"+(hasPlayers?"已包含世界、联机角色记录和存在的历史备份。":"此存档没有联机角色记录，已包含世界文件和现有历史备份。"),"导出完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
 }
 internal static void RequireInactiveSave(string selectedName,bool serverRunning,string activeName) {
  if(serverRunning&&(string.IsNullOrEmpty(activeName)||string.Equals(selectedName,activeName,StringComparison.OrdinalIgnoreCase)))throw new Exception("当前存档正在使用或状态未确认。请先点“保存并停止”，等服务器退出后再删除或改名。");
 }
 SettingsDraft CaptureDraft() {
  if(!settingsLoaded)throw new Exception("配置没有完整读取，已禁止自动开服和保存，防止覆盖原设置。请检查配置后重新打开助手。");
  var choice=saves.SelectedItem as SaveChoice;if(choice==null||!ValidSaveName(choice.Name))throw new Exception("请先选择存档，或点“新建存档”。");
  if(choice.Source!=null&&!File.Exists(choice.Source))throw new Exception("选中的存档已不存在，请刷新列表。");
  if(choice.Source==null&&!File.Exists(Path.Combine(SavesDir,choice.Name+".sav"))) {
   if(HasSaveFiles(choice.Name))throw new Exception("新档名称对应的旧角色记录已经存在，请选择其他名称，避免混用不同战役。");
   if(!File.Exists(Path.Combine(SavesDir,"default_new_game.sav")))throw new Exception("缺少用于创建新世界的初始模板。");
  }
  ValidatePassword(password.Text);
  var draft=new SettingsDraft{Save=choice,Autosave=(int)autosave.Value,Birth=birth.Checked,Perks=perks.Checked,Password=password.Text};
  foreach(var key in DifficultyKeys){int i=difficulty[key].SelectedIndex;if(i<0||i>=Levels.Length)throw new Exception("请选择有效难度。");draft.Difficulty.Add(key,Levels[i]);}return draft;
 }
 internal static string BuildServerConfig(string original,SettingsDraft draft) {
  ValidatePassword(draft.Password);
  string text=Jsonc.Set(original,"saveName",draft.Save.Name);text=Jsonc.Set(text,"autosaveMinutes",draft.Autosave);
  text=Jsonc.Set(text,"password",draft.Password??"");text=Jsonc.Set(text,"steam",true);text=Jsonc.Set(text,"logFile",true);text=Jsonc.Set(text,"port",4200);Jsonc.Parse(text);return text;
 }
 internal static void ValidatePassword(string value){if(value!=null&&value.Length>128)throw new Exception("房间密码最多 128 个字符，请缩短后保存。");}
 static string PasswordState(bool? enabled){return !enabled.HasValue?"密码状态待确认":enabled.Value?"已设置密码":"无密码";}
 internal static string BuildModConfig(string original,SettingsDraft draft) {
  string text=original;foreach(var pair in draft.Difficulty)text=Jsonc.Set(text,pair.Key,pair.Value);
  text=Jsonc.Set(text,"birthAndDeath",draft.Birth);text=Jsonc.Set(text,"autoAllocateClanMemberPerks",draft.Perks);Jsonc.Parse(text);return text;
 }
 void ApplySettings(SettingsDraft draft) {
  string backup=SaveServerSettings(HostInstallation.Current,draft);
  LogEvent("设置已备份至 "+backup+"；选档="+draft.Save.Name+"；大厅=public；"+PasswordState(!string.IsNullOrEmpty(draft.Password))+"。");
 }
 internal static string SaveServerSettings(HostInstallation install,SettingsDraft draft) {
  string configPath=install.ConfigPath,modConfigPath=install.ModConfigPath,savesDir=install.SavesDir;
  string original=File.ReadAllText(configPath),modOriginal=File.ReadAllText(modConfigPath);
  string config=BuildServerConfig(original,draft),mod=BuildModConfig(modOriginal,draft);
  string currentName=Jsonc.Scalar(original,"saveName") as string;
  string backup=Path.Combine(install.DataDir,"launcher-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-public-settings-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(backup);
  File.Copy(configPath,Path.Combine(backup,"server-config.json"));File.Copy(modConfigPath,Path.Combine(backup,"mod-config.json"));
  foreach(var name in new[]{currentName,draft.Save.Name}.Distinct(StringComparer.OrdinalIgnoreCase)) {
   if(string.IsNullOrEmpty(name)||!ValidSaveName(name))continue;
   foreach(var ext in new[]{".sav",".json"}){string f=Path.Combine(savesDir,name+ext);if(File.Exists(f))File.Copy(f,Path.Combine(backup,Path.GetFileName(f)),true);}
  }
  string dest=Path.Combine(savesDir,draft.Save.Name+".sav");
  if(draft.Save.Source!=null&&!string.Equals(Path.GetFullPath(draft.Save.Source),Path.GetFullPath(dest),StringComparison.OrdinalIgnoreCase)) {
   if(File.Exists(dest)||File.Exists(Path.ChangeExtension(dest,".json")))throw new Exception("服务器中已出现同名存档或角色记录，已取消导入，防止覆盖。请刷新存档列表。");
   Directory.CreateDirectory(savesDir);File.Copy(draft.Save.Source,dest,false);
   string json=Path.ChangeExtension(draft.Save.Source,".json");if(File.Exists(json))File.Copy(json,Path.ChangeExtension(dest,".json"),false);
   draft.Save.Source=dest;
  }
  // Client connection history is not part of dedicated-server configuration.
  // Never read or rewrite coop_options.json here: its fields vary across client versions.
  try{WriteAtomic(configPath,config);WriteAtomic(modConfigPath,mod);}
  catch{WriteAtomic(configPath,original);WriteAtomic(modConfigPath,modOriginal);throw;}
  return backup;
 }
 static void WriteAtomic(string path,string text) {
  string temp=path+".launcher-"+Guid.NewGuid().ToString("N")+".tmp";
  File.WriteAllText(temp,text,new UTF8Encoding(false));try{if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 Process FindServer() {
  foreach(var p in Process.GetProcessesByName("BannerlordCoopServer")) {try{if(string.Equals(p.MainModule.FileName,serverExe,StringComparison.OrdinalIgnoreCase))return p;}catch{}p.Dispose();}return null;
 }
 void StartServer(SettingsDraft draft) {
  if(closeRequested||pendingRestart!=null||pendingExport!=null||pendingInstantSave!=null)throw new Exception("请等待当前存档操作或关闭流程结束。");
  using(var existing=FindServer()){if(existing!=null){serverStarted=existing.StartTime;trackedPid=existing.Id;RefreshStatus();info.Text="已连接正在运行的服务器。修改设置后，请点“保存并重启”。";return;}}
  string[] installationErrors=HostBootstrap.Inspect(HostInstallation.Current);if(installationErrors.Length>0)throw new Exception(string.Join("\r\n",installationErrors)+"\r\n请在“本机安装设置”中检查或重新适配。");
  if(!File.Exists(serverExe))throw new Exception("找不到服务器程序。Steam 创意工坊安装位置可能已改变。");
  if(IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(x=>x.Port==4200))throw new Exception("UDP 4200 已被其他程序占用，请先关闭原服务器窗口。");
  if(Process.GetProcessesByName("steam").Length==0){Process.Start(new ProcessStartInfo(SteamExe){UseShellExecute=true});info.Text="Steam 已打开，请登录并在线，然后点“启动服务器”。";return;}
  string steamRuntimeNotice="";
  try{var repaired=SteamRuntime.EnsureProcessRecord(SteamExe,Path.Combine(DataDir,"launcher-backups"));if(!string.IsNullOrEmpty(repaired))LogEvent(repaired);}
  catch(Exception error){steamRuntimeNotice="\r\n"+error.Message;LogEvent(error.Message);}
  ApplySettings(draft);serverStarted=DateTime.Now;currentLog="";candidateLobby="";runningSave=draft.Save.Name;runningHasPassword=null;
  // Plain dedicated mode uses the authoritative data folder, creates missing worlds from its template,
  // and advertises a public Steam lobby. /coopsave switches save storage and is deliberately omitted.
  var launch=ServerProcess(HostInstallation.Current);
  var p=Process.Start(launch);if(startedServer!=null)startedServer.Dispose();startedServer=p;trackedPid=p.Id;tabs.SelectedTab=consolePage;
  info.Text="正在加载“"+draft.Save.Name+"”，预计约一分钟。公开大厅 · "+PasswordState(!string.IsNullOrEmpty(draft.Password))+"。Steam 入口就绪后，好友可点资料页的“加入游戏”。"+steamRuntimeNotice;LogEvent("启动专用服务器，存档="+runningSave);RefreshStatus();
 }
 internal static ProcessStartInfo ServerProcess(HostInstallation install) {
  var launch=new ProcessStartInfo(Path.Combine(install.ServerDir,"BannerlordCoopServer.exe"),"--data-dir \""+install.DataDir+"\""){WorkingDirectory=install.ServerDir,UseShellExecute=false,CreateNoWindow=false,WindowStyle=ProcessWindowStyle.Hidden};
  // Pin only this child's data locations; inherited global overrides must not redirect worlds or difficulty.
  launch.EnvironmentVariables["BANNERLORD_USER_DIR"]=install.DataDir;
  launch.EnvironmentVariables["COOP_DATA_DIR"]=Path.GetDirectoryName(install.ModConfigPath);
  return launch;
 }
 void BeginRestart() {
  if(pendingExport!=null||pendingInstantSave!=null)throw new Exception("正在保存存档，请等待任务结束。");
  var draft=CaptureDraft();using(var p=FindServer()){if(p==null){StartServer(draft);return;}pendingRestart=draft;restartDeadline=DateTime.Now.AddSeconds(90);ConsoleInput.Send((uint)p.Id,"stop");}
  tabs.Enabled=false;info.Text="先保存当前战役并停止服务器，退出后自动应用设置、启动选中的存档。";LogEvent("请求保存并重启至 "+draft.Save.Name);
 }
 void StopServer() {
  if(pendingExport!=null||pendingInstantSave!=null)throw new Exception("正在保存存档，请等待任务结束。");
  pendingRestart=null;tabs.Enabled=!closeRequested;using(var p=FindServer()){if(p==null){RefreshStatus();return;}ConsoleInput.Send((uint)p.Id,"stop");}
  info.Text="已请求保存并停止，请等待服务器保存后退出。";LogEvent("请求保存并停止");
 }
 void OpenGame(){Process.Start(new ProcessStartInfo("steam://rungameid/261550"){UseShellExecute=true});}
 void RefreshStatus() {
  try {
   if(!installationReady){UpdateServerConsole(null,"");ready=false;candidateLobby="";start.Enabled=saveNow.Enabled=restart.Enabled=stop.Enabled=exportSave.Enabled=deleteSave.Enabled=renameSave.Enabled=false;localState.Text="本机服务器：等待安装适配";localState.ForeColor=Color.DarkGoldenrod;steamState.Text="Steam 公开大厅：完成适配后可开服";steamState.ForeColor=Color.DarkGoldenrod;notice.Text="请在“本机安装设置”中检查检测结果。";return;}
   using(var p=FindServer()) {
    bool running=p!=null;bool busy=closeRequested||pendingRestart!=null||pendingExport!=null||pendingInstantSave!=null;start.Enabled=settingsLoaded&&!running&&!busy&&saves.SelectedItem!=null;saveNow.Enabled=running&&ready&&!busy;restart.Enabled=settingsLoaded&&!busy&&saves.SelectedItem!=null;stop.Enabled=running&&!busy;exportSave.Enabled=!busy&&saves.SelectedItem!=null;deleteSave.Enabled=!busy&&saves.SelectedItem!=null;renameSave.Enabled=!busy&&saves.SelectedItem!=null;
    if(pendingExport!=null) {
     if(running&&p.Id!=pendingExport.ProcessId){pendingExport=null;tabs.Enabled=!closeRequested;info.Text="服务器进程已改变，已取消导出。请等待当前服务器就绪后重试。";}
     else if(!running&&!PortInUse()){var job=pendingExport;pendingExport=null;tabs.Enabled=!closeRequested;Guard(()=>{RequireExportSaved(job);CompleteExport(job.Name,job.Path);});}
     else if(DateTime.Now>pendingExport.Deadline){pendingExport=null;tabs.Enabled=!closeRequested;info.Text="服务器还未完全退出，已取消导出，避免复制未保存的战役。请等服务器保存退出后重试。";}
    }
    if(!running) {
     UpdateServerConsole(null,"");
     PollInstantSave(null,"");
     ready=false;candidateLobby="";currentLog="";trackedPid=0;runningSave="";runningHasPassword=null;localState.Text="本机服务器：未运行";localState.ForeColor=Color.Firebrick;
     steamState.Text="Steam 公开大厅：等待开服";steamState.ForeColor=Color.DarkGoldenrod;notice.Text="选档 "+SelectedName()+" · 修改会在下次启动时生效";
     if(pendingRestart!=null){var draft=pendingRestart;pendingRestart=null;tabs.Enabled=!closeRequested;StartServer(draft);}return;
    }
    if(pendingRestart!=null&&DateTime.Now>restartDeadline){pendingRestart=null;tabs.Enabled=!closeRequested;info.Text="服务器还没有退出，已取消自动重启。请查看“服务器控制台”，避免重复开服。";}
    if(trackedPid!=p.Id){trackedPid=p.Id;serverStarted=p.StartTime;currentLog="";candidateLobby="";runningSave="";runningHasPassword=null;}
    if(serverStarted==DateTime.MinValue)serverStarted=p.StartTime;
    if(string.IsNullOrEmpty(currentLog)) {
     var dir=new DirectoryInfo(Path.Combine(DataDir,"logs"));
     if(dir.Exists){var file=dir.GetFiles("coop-server-*.log").Where(f=>f.CreationTime>=serverStarted.AddSeconds(-10)).OrderByDescending(f=>f.LastWriteTime).FirstOrDefault();if(file!=null)currentLog=file.FullName;}
    }
    string log="";if(!string.IsNullOrEmpty(currentLog))using(var stream=new FileStream(currentLog,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream)){log=reader.ReadToEnd();}
    UpdateServerConsole(p,log);
    bool failed=log.Contains("server exited unexpectedly")||log.Contains("session ended (exit");
    ready=!failed&&Regex.IsMatch(log,@"^\d\d:\d\d:\d\d\.\d+\s+\[DedicatedServer\] SERVING(?:\s|$)",RegexOptions.Multiline);
    localState.Text=failed?"本机服务器：已退出，请关闭原窗口后重试":ready?"本机服务器：已就绪 · 127.0.0.1:4200":"本机服务器：正在加载战役";localState.ForeColor=ready?Color.DarkGreen:failed?Color.Firebrick:Color.DarkGoldenrod;
    var matches=Regex.Matches(log,"Steam lobby \"([0-9]+)\" created");candidateLobby=matches.Count>0&&!failed?matches[matches.Count-1].Groups[1].Value:"";
    if(candidateLobby!=""){steamState.Text="Steam 公开大厅：已创建 · "+candidateLobby;steamState.ForeColor=Color.DarkGreen;}
    else if(log.Contains("k_EResultNoConnection")){steamState.Text="Steam 公开大厅：登录失败，服务器仍在重试";steamState.ForeColor=Color.Firebrick;}
    else{steamState.Text="Steam 公开大厅：等待 Steam 登录与房间创建";steamState.ForeColor=Color.DarkGoldenrod;}
    var pulses=Regex.Matches(log,@"\[DedicatedServer\] pulse:.*?players=(\d+)");string players=pulses.Count>0?pulses[pulses.Count-1].Groups[1].Value:"0";
    ReadRunningState(log,out runningSave,out runningHasPassword);
    PollInstantSave(p,log);
    saveNow.Enabled=!closeRequested&&ready&&!IsInstantSavePending&&pendingRestart==null&&pendingExport==null&&ValidSaveName(runningSave);
    notice.Text="当前服务器 · "+(string.IsNullOrEmpty(runningSave)?"存档以服务器配置为准":runningSave)+" · 在线 "+players+" 人 · "+PasswordState(runningHasPassword);
   }
  }catch(Exception e){info.Text="状态读取失败："+e.Message;PollInstantSaveReadError();}
 }
 internal static void ReadRunningState(string log,out string save,out bool? hasPassword) {
  save="";hasPassword=null;var states=Regex.Matches(log,"@DS@(\\{\"ev\":\"state\"[^\\r\\n]*\\})");
  if(states.Count==0)return;var data=Jsonc.Parse(states[states.Count-1].Groups[1].Value) as Dictionary<string,object>;if(data==null)return;
  if(data.ContainsKey("save"))save=Convert.ToString(data["save"]);if(data.ContainsKey("pw")&&data["pw"] is bool)hasPassword=(bool)data["pw"];
 }
 internal static bool HasWorldSaved(string log,string name) {
  return Regex.IsMatch(log,@"^\d\d:\d\d:\d\d\.\d+[ \t]+\[DedicatedServer\][ \t]+(?:autosave|operator save|shutdown save):[ \t]+world saved to '"+Regex.Escape(name+".sav")+@"'[ \t]+\(\d+ ms\)[ \t]*\r?$",RegexOptions.Multiline);
 }
 internal static void RequireExportSaved(PendingSaveExport job) {
  string tail="";
  if(File.Exists(job.LogPath))using(var stream=new FileStream(job.LogPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
   if(stream.Length>=job.LogOffset){stream.Seek(job.LogOffset,SeekOrigin.Begin);using(var reader=new StreamReader(stream,Encoding.UTF8)){tail=reader.ReadToEnd();}}
  }
  if(!HasWorldSaved(tail,job.Name))throw new Exception("服务器已退出，但未确认本次战役保存成功，已取消自动导出。原存档仍保留；请查看服务器日志，确认保存后再导出。");
 }
 void LogEvent(string text){try{File.AppendAllText(Path.Combine(DataDir,"launcher-events.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+text+Environment.NewLine,new UTF8Encoding(false));}catch{}}
}

internal sealed class SaveChoice {public string Name;public string Source;public override string ToString(){return Name+(Source==null?"  [新战役 · 开服时创建]":"");}}
internal sealed class PendingSaveExport {public string Name,Path,LogPath;public long LogOffset;public int ProcessId;public DateTime Deadline;}
internal sealed class SettingsDraft {public SaveChoice Save;public int Autosave;public bool Birth,Perks;public string Password="";public readonly Dictionary<string,string> Difficulty=new Dictionary<string,string>();}
internal sealed class NewSaveDialog : Form {
 readonly TextBox name=new TextBox();public string SaveName{get{return name.Text.Trim();}}
 public NewSaveDialog(){Text="新建联机战役";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(480,198);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  var label=new Label{Text="新档名称（原有战役会保留）",Location=new Point(18,18),AutoSize=true};name.SetBounds(20,52,440,30);name.Text="新战役-"+DateTime.Now.ToString("MMdd-HHmm");
  var description=new Label{Text="使用 MOD 自带初始世界，开服后玩家创建自己的角色。\r\n难度使用主窗口“详细难度”中的设置。",Location=new Point(18,94),Size=new Size(443,44),ForeColor=Color.DimGray};
  var ok=new Button{Text="选择新档",DialogResult=DialogResult.OK};ok.SetBounds(243,149,106,34);var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel};cancel.SetBounds(361,149,99,34);Controls.AddRange(new Control[]{label,name,description,ok,cancel});AcceptButton=ok;CancelButton=cancel;
 }
}

internal static class Jsonc {
 static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer();
 // Retain offsets while masking JSONC comments and trailing commas. Strings remain intact.
 internal static string Clean(string text){char[] result=text.ToCharArray();bool str=false,escape=false,line=false,block=false;
  for(int i=0;i<text.Length;i++) {
   char c=text[i];if(line){if(c=='\r'||c=='\n')line=false;else result[i]=' ';continue;}
   if(block){result[i]=c=='\r'||c=='\n'?c:' ';if(c=='*'&&i+1<text.Length&&text[i+1]=='/'){result[++i]=' ';block=false;}continue;}
   if(str){if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')str=false;continue;}
   if(c=='"'){str=true;continue;}if(c=='/'&&i+1<text.Length&&text[i+1]=='/'){result[i]=' ';result[++i]=' ';line=true;}else if(c=='/'&&i+1<text.Length&&text[i+1]=='*'){result[i]=' ';result[++i]=' ';block=true;}
  }
  if(str||block)throw new Exception("配置文件存在未结束的字符串或注释。");
  str=false;escape=false;for(int i=0;i<result.Length;i++){char c=result[i];if(str){if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='"')str=false;continue;}if(c=='"'){str=true;continue;}if(c==','){int j=i+1;while(j<result.Length&&char.IsWhiteSpace(result[j]))j++;if(j<result.Length&&(result[j]=='}'||result[j]==']'))result[i]=' ';}}
  return new string(result);
 }
 internal static object Parse(string text){return Serializer.DeserializeObject(Clean(text));}
 static Match Find(string text,string key){var matches=Regex.Matches(Clean(text),"\""+Regex.Escape(key)+"\"\\s*:\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|-?[0-9]+(?:\\.[0-9]+)?)");if(matches.Count!=1)throw new Exception("配置字段缺失或重复："+key+"。请核对 MOD 版本。");return matches[0];}
 internal static object Scalar(string text,string key){var match=Find(text,key);return Serializer.DeserializeObject(match.Groups["value"].Value);}
 internal static string Set(string text,string key,object value){var found=Find(text,key).Groups["value"];return text.Substring(0,found.Index)+Serializer.Serialize(value)+text.Substring(found.Index+found.Length);}
}

internal static class LauncherChecks {
 internal static void Run(string reportPath){var results=new List<string>();try{
  string original=HostBootstrap.NewServerConfig;
  var difficultyFixture=new Dictionary<string,object>();foreach(var key in CoopLauncher.DifficultyKeys)difficultyFixture[key]="VeryEasy";difficultyFixture["birthAndDeath"]=true;difficultyFixture["autoAllocateClanMemberPerks"]=false;
  string mod=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"difficulty",difficultyFixture},{"modOptions",new Dictionary<string,object>{{"battleSize",800},{"clientsCanUseCheats",false}}}});
  var draft=new SettingsDraft{Save=new SaveChoice{Name="验证新档"},Autosave=7,Birth=false,Perks=true,Password=""};
  for(int i=0;i<CoopLauncher.DifficultyKeys.Length;i++)draft.Difficulty.Add(CoopLauncher.DifficultyKeys[i],new[]{"VeryEasy","Easy","Realistic"}[i%3]);
  string next=CoopLauncher.BuildServerConfig(original,draft),nextMod=CoopLauncher.BuildModConfig(mod,draft);
  if(!Equals(Jsonc.Scalar(next,"saveName"),draft.Save.Name)||!Equals(Jsonc.Scalar(next,"password"),"")||!Equals(Jsonc.Scalar(next,"steam"),true))throw new Exception("Server settings round trip failed");results.Add("PASS 新档名称、无密码与 Steam 配置可往返读取");
  foreach(var value in new[]{"898989"," Changed-Password ","引号\"与\\路径",new string('x',128),""}){draft.Password=value;string pwConfig=CoopLauncher.BuildServerConfig(original,draft);if(!Equals(Jsonc.Scalar(pwConfig,"password"),value))throw new Exception("Password round trip failed");}
  bool oversizedRejected=false;try{CoopLauncher.ValidatePassword(new string('x',129));}catch{oversizedRejected=true;}if(!oversizedRejected)throw new Exception("Oversized password accepted");results.Add("PASS 新增、修改、取消密码及128字符边界；空格和特殊字符原样保留");
  string activeSave;bool? activePw;CoopLauncher.ReadRunningState("@DS@{\"ev\":\"state\",\"phase\":\"boot\",\"save\":\"old\",\"pw\":false}\n@DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"current\",\"pw\":true}",out activeSave,out activePw);if(activeSave!="current"||activePw!=true)throw new Exception("Latest running password state failed");CoopLauncher.ReadRunningState("",out activeSave,out activePw);if(activePw.HasValue)throw new Exception("Missing running state incorrectly reported");results.Add("PASS 密码状态使用服务器实际生效值，缺少运行状态不误报");
  foreach(var pair in draft.Difficulty)if(!Equals(Jsonc.Scalar(nextMod,pair.Key),pair.Value))throw new Exception("Difficulty round trip failed "+pair.Key);if(!Equals(Jsonc.Scalar(nextMod,"birthAndDeath"),false)||!Equals(Jsonc.Scalar(nextMod,"autoAllocateClanMemberPerks"),true))throw new Exception("Difficulty toggles failed");results.Add("PASS 9项独立难度及2项开关可往返读取");
  if(!Equals(Jsonc.Scalar(nextMod,"battleSize"),Jsonc.Scalar(mod,"battleSize"))||!Equals(Jsonc.Scalar(nextMod,"clientsCanUseCheats"),Jsonc.Scalar(mod,"clientsCanUseCheats")))throw new Exception("Unrelated options changed");results.Add("PASS 其他玩法设置保持原值");
  string probe="{ // \"a\": 999\n\"a\": \"//comma,}\", /* \"a\": 1 */ \"b\": true, }";string changed=Jsonc.Set(probe,"a","转义\"与\\路径");Jsonc.Parse(changed);if(!Equals(Jsonc.Scalar(changed,"a"),"转义\"与\\路径"))throw new Exception("JSONC escaped string failed");results.Add("PASS JSONC注释、尾随逗号及字符串转义处理");
  foreach(var invalid in new[]{"../outside","..","CON","NUL","bad/name","bad\\name","name.","default_new_game","save.backup1"})if(CoopLauncher.ValidSaveName(invalid))throw new Exception("Unsafe save name accepted "+invalid);if(!CoopLauncher.ValidSaveName("新战役-01"))throw new Exception("Valid name rejected");results.Add("PASS 路径与保留文件名防覆盖检查");
  SaveStorage.Check(reportPath,results);
  InstantSaveConfirmation.Check(reportPath,results);
  HostInstallation.Check(reportPath,results);HostBootstrap.Check(reportPath,results);
  ServerConsoleChecks.Check(results);LauncherShutdownChecks.Check(reportPath,results);
  CheckClientPreferences(reportPath,original,mod,results);
  string savedLine="20:10:01.234 [DedicatedServer] autosave: world saved to '测试战役.sav' (1273 ms)\r\n";
  if(!CoopLauncher.HasWorldSaved(savedLine,"测试战役")||CoopLauncher.HasWorldSaved(savedLine,"测试")||CoopLauncher.HasWorldSaved("", "测试战役")||CoopLauncher.HasWorldSaved("startup help: "+savedLine,"测试战役"))throw new Exception("Fresh saved world confirmation failed");
  if(!CoopLauncher.HasWorldSaved(savedLine.Replace("autosave:","shutdown save:"),"测试战役")||!CoopLauncher.HasWorldSaved(savedLine.Replace("autosave:","operator save:"),"测试战役"))throw new Exception("Real dedicated host save types not recognised");
  string logFixture=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"save-confirmation-"+Guid.NewGuid().ToString("N")+".log");File.WriteAllText(logFixture,savedLine,new UTF8Encoding(false));var job=new PendingSaveExport{Name="测试战役",LogPath=logFixture,LogOffset=new FileInfo(logFixture).Length};bool staleBlocked=false;try{CoopLauncher.RequireExportSaved(job);}catch{staleBlocked=true;}if(!staleBlocked)throw new Exception("Earlier autosave incorrectly confirmed export");File.AppendAllText(logFixture,savedLine,new UTF8Encoding(false));CoopLauncher.RequireExportSaved(job);results.Add("PASS 停服导出只接受本次请求后的对应世界保存成功日志，旧保存和其他存档不误报");
  results.Add("PASS 使用内存及独立测试目录，真实配置与战役存档未修改");
 }catch(Exception e){results.Add("FAIL "+e.Message);Environment.ExitCode=1;}File.WriteAllLines(reportPath,results,new UTF8Encoding(false));}
 static void CheckClientPreferences(string reportPath,string serverConfig,string modConfig,List<string> results) {
  string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),"c-"+Guid.NewGuid().ToString("N").Substring(0,8));
  var install=new HostInstallation{DocumentsOverride=root};Directory.CreateDirectory(install.SavesDir);
  File.WriteAllText(install.ConfigPath,serverConfig,new UTF8Encoding(false));File.WriteAllText(install.ModConfigPath,modConfig,new UTF8Encoding(false));
  string world=Path.Combine(install.SavesDir,"兼容战役.sav"),players=Path.ChangeExtension(world,".json");
  byte[] worldData={0,1,2,254,255},playerData=Encoding.UTF8.GetBytes("{\"Players\":[],\"UniqueGameId\":\"existing\"}");File.WriteAllBytes(world,worldData);File.WriteAllBytes(players,playerData);
  string clientPath=Path.Combine(install.GameUserDir,"Configs","BannerlordCoop","coop_options.json");Directory.CreateDirectory(Path.GetDirectoryName(clientPath));
  string[] cases={null,"{}","{\"lastServerAddress\":\"a\",\"lastServerAddress\":\"b\"}","{\"nested\":{\"lastServerAddress\":\"remote\"}}","{ broken JSON","{\"lastServerAddress\":42}","{\"lastServerAddress\":\"192.168.1.20\",\"other\":true}"};
  var draft=new SettingsDraft{Save=new SaveChoice{Name="兼容战役"},Autosave=5,Birth=false,Perks=true,Password="keep-room-password"};foreach(string key in CoopLauncher.DifficultyKeys)draft.Difficulty.Add(key,"Easy");
  for(int i=0;i<cases.Length;i++) {
   if(cases[i]!=null)File.WriteAllText(clientPath,cases[i],new UTF8Encoding(true));
   byte[] before=File.Exists(clientPath)?File.ReadAllBytes(clientPath):null;draft.Autosave=5+i;
   string backup=CoopLauncher.SaveServerSettings(install,draft);
   if(!Equals(Jsonc.Scalar(File.ReadAllText(install.ConfigPath),"saveName"),"兼容战役")||!Equals(Jsonc.Scalar(File.ReadAllText(install.ConfigPath),"autosaveMinutes"),draft.Autosave)||!Equals(Jsonc.Scalar(File.ReadAllText(install.ModConfigPath),"combatAIDifficulty"),"Easy"))throw new Exception("Client preference incorrectly blocked server settings");
   if(before==null?File.Exists(clientPath):!before.SequenceEqual(File.ReadAllBytes(clientPath)))throw new Exception("Unrelated client preferences changed");
   if(!File.Exists(Path.Combine(backup,"server-config.json"))||!File.Exists(Path.Combine(backup,"mod-config.json")))throw new Exception("Server configuration backups missing");
   if(!worldData.SequenceEqual(File.ReadAllBytes(world))||!playerData.SequenceEqual(File.ReadAllBytes(players)))throw new Exception("Existing campaign data changed during settings compatibility check");
  }
  results.Add("PASS 新电脑无客户端配置、缺失/重复/嵌套地址、错误类型及无效JSON均不阻止保存开服设置；客户端内容与战役原样保留");
  using(var locked=new FileStream(clientPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){CoopLauncher.SaveServerSettings(install,draft);}
  results.Add("PASS 客户端配置独占锁定时仍能保存专用服务器设置，核心配置备份完整");
  string validServer=File.ReadAllText(install.ConfigPath),validMod=File.ReadAllText(install.ModConfigPath);string invalidServer=validServer.Replace("\"password\":", "\"removedPassword\":");File.WriteAllText(install.ConfigPath,invalidServer,new UTF8Encoding(false));
  bool rejected=false;try{CoopLauncher.SaveServerSettings(install,draft);}catch{rejected=true;}if(!rejected||File.ReadAllText(install.ConfigPath)!=invalidServer||File.ReadAllText(install.ModConfigPath)!=validMod)throw new Exception("Required server configuration validation weakened");
  results.Add("PASS 必需的服务器字段仍严格检查，错误配置不被覆盖");
 }
}

internal static partial class ConsoleInput {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool FreeConsole();
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool AttachConsole(uint pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr sec,uint creation,uint flags,IntPtr template);
 [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=20)]struct Input{[FieldOffset(0)]public ushort Type;[FieldOffset(4)]public int Down;[FieldOffset(8)]public ushort Repeat;[FieldOffset(10)]public ushort Key;[FieldOffset(12)]public ushort Scan;[FieldOffset(14)]public char Character;[FieldOffset(16)]public uint State;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool WriteConsoleInput(IntPtr h,Input[] inputs,uint count,out uint written);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
 public static void Send(uint pid,string command){
  if(command==null||command.Length>1024||command.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new ArgumentException("请一次发送一条命令，最多1024个字符。");
  string manual="请在“服务器控制台”页显示独立窗口后手动输入 "+command+"。";
  FreeConsole();if(!AttachConsole(pid))throw new Exception("无法向服务器发送指令，"+manual);
  var h=CreateFile("CONIN$",0x40000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
  if(h==new IntPtr(-1)){FreeConsole();throw new Exception("无法访问服务器输入，"+manual);}
  try{
   string text=command+"\r";var input=new Input[text.Length*2];
   for(int i=0;i<text.Length;i++){input[i*2]=new Input{Type=1,Down=1,Repeat=1,Character=text[i],Key=(ushort)(text[i]=='\r'?13:char.ToUpperInvariant(text[i]))};input[i*2+1]=input[i*2];input[i*2+1].Down=0;}
   uint n;if(!WriteConsoleInput(h,input,(uint)input.Length,out n)||n!=(uint)input.Length)throw new Exception("服务器指令发送失败，"+manual);
  }finally{CloseHandle(h);FreeConsole();}
 }
}
