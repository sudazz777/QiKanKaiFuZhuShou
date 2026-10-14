using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class CoopLauncher {
 readonly TabPage consolePage=new TabPage("服务器控制台");
 readonly TextBox consoleOutput=new TextBox(),consoleCommand=new TextBox();
 readonly CheckBox consoleRefresh=new CheckBox(),consoleExternal=new CheckBox();
 readonly Label consoleHint=new Label();
 readonly Button consoleSend=new Button();
 int consoleProcessId;
 bool updatingConsoleWindow;

 void BuildConsolePage() {
  consolePage.BackColor=Color.White;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(10)};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
  layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
  var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
  consoleRefresh.Text="自动刷新";consoleRefresh.Checked=true;consoleRefresh.AutoSize=true;
  consoleExternal.Text="显示独立窗口";consoleExternal.AutoSize=true;
  consoleExternal.CheckedChanged+=(s,e)=>{
   if(updatingConsoleWindow||preview)return;
   Guard(()=>{using(var process=FindServer()){if(process!=null)ConsoleInput.SetVisible((uint)process.Id,consoleExternal.Checked);}});
  };
  consoleHint.AutoSize=true;consoleHint.ForeColor=Color.DimGray;consoleHint.Margin=new Padding(12,5,0,0);consoleHint.Text="等待服务器启动";
  toolbar.Controls.AddRange(new Control[]{consoleRefresh,consoleExternal,consoleHint});
  consoleOutput.Multiline=true;consoleOutput.ReadOnly=true;consoleOutput.WordWrap=false;
  consoleOutput.ScrollBars=ScrollBars.Both;consoleOutput.Dock=DockStyle.Fill;
  consoleOutput.BackColor=Color.FromArgb(22,26,31);consoleOutput.ForeColor=Color.Gainsboro;
  consoleOutput.Font=new Font("Consolas",9);consoleOutput.Text="服务器日志与交互提示将在这里显示。\r\n如需输入命令或协议确认，请阅读提示后在下方输入并发送。";
  var input=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,5,0,0)};
  input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));input.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,94));
  consoleCommand.Dock=DockStyle.Fill;consoleCommand.MaxLength=1024;consoleCommand.Enabled=false;
  consoleCommand.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Guard(SendConsoleCommand);}};
  consoleSend.Text="发送命令";consoleSend.Dock=DockStyle.Fill;consoleSend.Enabled=false;
  consoleSend.Click+=(s,e)=>Guard(SendConsoleCommand);
  input.Controls.Add(consoleCommand,0,0);input.Controls.Add(consoleSend,1,0);
  layout.Controls.Add(toolbar,0,0);layout.Controls.Add(consoleOutput,0,1);layout.Controls.Add(input,0,2);consolePage.Controls.Add(layout);
  tabs.TabPages.Add(consolePage);
 }

 void SendConsoleCommand() {
  if(preview||!consoleSend.Enabled)throw new Exception("请等待服务器启动或当前存档操作完成。");
  string command=consoleCommand.Text;if(string.IsNullOrWhiteSpace(command))return;
  using(var process=FindServer()){
   if(process==null)throw new Exception("服务器未运行。");
   ConsoleInput.Send((uint)process.Id,command);
  }
  consoleCommand.Clear();consoleHint.Text="命令已发送，请查看服务器响应";
 }

 void UpdateServerConsole(Process process,string log) {
  bool running=process!=null&&!preview;
  bool busy=closeRequested||pendingRestart!=null||pendingExport!=null||pendingInstantSave!=null;
  consoleSend.Enabled=consoleCommand.Enabled=running&&!busy;consoleExternal.Enabled=running&&!ServerPipeSession.Contains((uint)process.Id);
  if(!running){consoleHint.Text=consoleProcessId==0?"等待服务器启动":"服务器已停止，保留最后输出";return;}
  if(consoleProcessId!=process.Id){
   consoleProcessId=process.Id;updatingConsoleWindow=true;
   try{consoleExternal.Checked=false;}finally{updatingConsoleWindow=false;}
   consoleOutput.Text="正在连接服务器控制台…";
  }
  try {
   string snapshot;bool piped=ServerPipeSession.TryRead((uint)process.Id,out snapshot);
   if(!piped)snapshot=ConsoleInput.ReadSnapshot((uint)process.Id,!consoleExternal.Checked);
   consoleHint.Text=busy?"正在执行存档操作":piped?"后台无窗口 · 可在下方输入命令":"最近 120 行 · 可在下方输入命令";
   if(consoleRefresh.Checked&&consoleOutput.Text!=snapshot){consoleOutput.Text=snapshot;consoleOutput.SelectionStart=consoleOutput.TextLength;consoleOutput.ScrollToCaret();}
  }catch(Exception){
   consoleHint.Text="控制台连接中；可显示独立窗口排查";
   if(consoleRefresh.Checked&&!string.IsNullOrEmpty(log)){
    consoleOutput.Text=log.Length>24000?log.Substring(log.Length-24000):log;
    consoleOutput.SelectionStart=consoleOutput.TextLength;consoleOutput.ScrollToCaret();
   }
  }
 }


}

internal static partial class ConsoleInput {
 [StructLayout(LayoutKind.Sequential)]struct Coord {public short X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct SmallRect {public short Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct ScreenInfo {public Coord Size,Cursor;public ushort Attributes;public SmallRect Window;public Coord MaximumWindow;}
 [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=4)]struct Cell {[FieldOffset(0)]public char Character;[FieldOffset(2)]public ushort Attributes;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetConsoleScreenBufferInfo(IntPtr output,out ScreenInfo info);
 [DllImport("kernel32.dll",EntryPoint="ReadConsoleOutputW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ReadConsoleOutput(IntPtr output,[Out]Cell[] text,Coord size,Coord position,ref SmallRect region);
 [DllImport("kernel32.dll")]static extern IntPtr GetConsoleWindow();
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int mode);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("kernel32.dll",EntryPoint="SetLastError")]static extern void ClearLastError(uint error);
 const long ToolWindowStyle=0x80,AppWindowStyle=0x40000;

 static long ExtendedStyle(IntPtr window) {
  ClearLastError(0);long style=GetWindowLongPtr(window,-20).ToInt64();int error=Marshal.GetLastWin32Error();
  if(style==0&&error!=0)throw new System.ComponentModel.Win32Exception(error);
  return style;
 }
 static void SuppressTaskbar(IntPtr window) {
  long style=ExtendedStyle(window),desired=(style&~AppWindowStyle)|ToolWindowStyle;
  if(style==desired)return;
  // Refresh only this server's console window; never change the launcher's window.
  bool visible=IsWindowVisible(window);ShowWindow(window,0);
  try {
   ClearLastError(0);var previous=SetWindowLongPtr(window,-20,new IntPtr(desired));int error=Marshal.GetLastWin32Error();
   if(previous==IntPtr.Zero&&error!=0)throw new System.ComponentModel.Win32Exception(error);
   if(!SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x37))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  }finally{if(visible)ShowWindow(window,8);}
 }

 internal static void SetVisible(uint pid,bool visible) {
  FreeConsole();if(!AttachConsole(pid))throw new Exception("暂时无法连接服务器控制台，请稍后重试。");
  try{var window=GetConsoleWindow();if(window!=IntPtr.Zero){SuppressTaskbar(window);ShowWindow(window,visible?5:0);}}
  finally{FreeConsole();}
 }
 internal static string ReadSnapshot(uint pid,bool hide) {
  FreeConsole();if(!AttachConsole(pid))throw new Exception("服务器控制台尚未就绪。");
  IntPtr output=new IntPtr(-1);
  try {
   var window=GetConsoleWindow();if(window!=IntPtr.Zero){if(hide)ShowWindow(window,0);SuppressTaskbar(window);}
   output=CreateFile("CONOUT$",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
   ScreenInfo screen;if(output==new IntPtr(-1)||!GetConsoleScreenBufferInfo(output,out screen)||screen.Size.X<=0)throw new Exception("暂时无法读取服务器输出。");
   int first=Math.Max(0,screen.Cursor.Y-119),rows=screen.Cursor.Y-first+1,width=screen.Size.X;
   var cells=new Cell[rows*width];var region=new SmallRect{Left=0,Top=(short)first,Right=(short)(width-1),Bottom=screen.Cursor.Y};
   if(!ReadConsoleOutput(output,cells,new Coord{X=(short)width,Y=(short)rows},new Coord{X=0,Y=0},ref region))throw new Exception("服务器输出读取失败。");
   var snapshot=new StringBuilder();
   for(int row=0;row<rows;row++){
    var line=new StringBuilder();for(int col=0;col<width;col++){var cell=cells[row*width+col];if((cell.Attributes&0x0200)==0)line.Append(cell.Character=='\0'?' ':cell.Character);}
    snapshot.Append(line.ToString().TrimEnd(' '));if(row<rows-1)snapshot.Append("\r\n");
   }
   return snapshot.ToString();
  }finally{if(output!=new IntPtr(-1))CloseHandle(output);FreeConsole();}
 }
 internal static bool HasConsole(uint pid) {
  FreeConsole();bool attached=AttachConsole(pid);if(attached)FreeConsole();return attached;
 }
 internal static bool WindowVisible(uint pid) {
  FreeConsole();if(!AttachConsole(pid))throw new Exception("Fixture console not available");
  try{return IsWindowVisible(GetConsoleWindow());}finally{FreeConsole();}
 }
 internal static bool TaskbarSuppressed(uint pid) {
  FreeConsole();if(!AttachConsole(pid))throw new Exception("Fixture console not available");
  try{var window=GetConsoleWindow();if(window==IntPtr.Zero)return false;long style=ExtendedStyle(window);return (style&ToolWindowStyle)!=0&&(style&AppWindowStyle)==0;}
  finally{FreeConsole();}
 }
}

internal static class ServerConsoleChecks {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool AllocConsole();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool WriteConsoleW(IntPtr output,string text,uint length,out uint written,IntPtr reserved);
 [DllImport("kernel32.dll")]static extern IntPtr GetStdHandle(int id);
 internal static void RunFixture(string marker) {
  if(!AllocConsole()){Environment.ExitCode=1;return;}
  Write("FIXTURE 中文服务器日志\r\n请输入命令：\r\n");
  string command;
  while((command=Console.ReadLine())!=null){Write("COMMAND "+command+"\r\n");if(command=="stop"){Thread.Sleep(250);if(!string.IsNullOrEmpty(marker))File.WriteAllText(marker,"saved");return;}if(command=="exit")return;}
 }
 static void Write(string text){uint written;if(!WriteConsoleW(GetStdHandle(-11),text,(uint)text.Length,out written,IntPtr.Zero))throw new IOException("Fixture output failed");}
 internal static string WaitFor(Process process,string expected) {
  var deadline=DateTime.UtcNow.AddSeconds(10);
  while(DateTime.UtcNow<deadline){
   if(process.HasExited)throw new Exception("Fixture exited unexpectedly");
   try{var snapshot=ConsoleInput.ReadSnapshot((uint)process.Id,true);if(snapshot.Contains(expected))return snapshot;}catch{if(process.HasExited)throw;}
   Thread.Sleep(100);
  }
  throw new Exception("Fixture output not received: "+expected);
 }
 internal static void Check(List<string> results) {
  bool invalid=false;try{ConsoleInput.Send(0,"save\r\nstop");}catch(ArgumentException){invalid=true;}if(!invalid)throw new Exception("Multiline console command accepted");
  using(var fixture=Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location,"--console-fixture"){UseShellExecute=false,CreateNoWindow=false,WindowStyle=ProcessWindowStyle.Hidden})){
   try {
    WaitFor(fixture,"FIXTURE 中文服务器日志");if(ConsoleInput.WindowVisible((uint)fixture.Id))throw new Exception("Console did not remain hidden");
    if(!ConsoleInput.TaskbarSuppressed((uint)fixture.Id))throw new Exception("Hidden console retained taskbar style");
    results.Add("PASS 隐藏控制台仍能读取中文日志与交互提示，不重定向或关闭服务器输入");
    ConsoleInput.Send((uint)fixture.Id,"ping");WaitFor(fixture,"COMMAND ping");
    results.Add("PASS 内嵌控制台通过原输入通道发送命令，服务器成功接收并响应；拒绝多行命令");
    ConsoleInput.SetVisible((uint)fixture.Id,true);if(!ConsoleInput.WindowVisible((uint)fixture.Id))throw new Exception("Console window was not restored");
    if(!ConsoleInput.TaskbarSuppressed((uint)fixture.Id))throw new Exception("Visible console regained taskbar style");
    results.Add("PASS 服务器控制台移除任务栏窗口样式，后台和手动显示时都不添加独立任务栏入口");
    results.Add("PASS 手动显示独立控制台时保留正在运行的服务器进程");
    ConsoleInput.Send((uint)fixture.Id,"exit");if(!fixture.WaitForExit(5000)||fixture.ExitCode!=0)throw new Exception("Fixture did not exit normally");
   }finally{if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit();}}
  }
 }
}
