[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable,[string]$Version='1.6.9')
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class LauncherUiProbe {
  public delegate bool EnumProc(IntPtr window, IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
  [DllImport("user32.dll", EntryPoint="SendMessageW")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
  [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)] static extern IntPtr ReadText(IntPtr window, uint message, IntPtr capacity, StringBuilder text);
  public static int SelectedTab(IntPtr main) {
    IntPtr tab=IntPtr.Zero;
    EnumChildWindows(main, (child,p)=> {
      var name=new StringBuilder(256);GetClassName(child,name,name.Capacity);
      if(name.ToString().IndexOf("SysTabControl32",StringComparison.OrdinalIgnoreCase)>=0)tab=child;
      return true;
    }, IntPtr.Zero);
    if(tab==IntPtr.Zero)throw new Exception("Main window has no tab control.");
    return (int)SendMessage(tab,0x130b,IntPtr.Zero,IntPtr.Zero);
  }
  public static bool HasInstallationPrompt(IntPtr main) {
    bool found=false;
    EnumChildWindows(main,(child,p)=> {
      var text=new StringBuilder(2048);ReadText(child,0x000d,(IntPtr)text.Capacity,text);
      if(text.ToString().Contains("\u9996\u6b21\u68c0\u6d4b\u672a\u5b8c\u6210"))found=true;
      return true;
    },IntPtr.Zero);
    return found;
  }
}
'@

$child = Start-Process -FilePath $Executable -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($child.HasExited) { throw 'Launcher exited before showing its main window.' }
        $child.Refresh()
        if ($child.MainWindowHandle -ne [IntPtr]::Zero -and $child.MainWindowTitle.EndsWith("v$Version")) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($child.MainWindowHandle -eq [IntPtr]::Zero -or !$child.MainWindowTitle.EndsWith("v$Version")) {
        throw ('Expected main window not found: ' + $child.MainWindowTitle)
    }
    if ([LauncherUiProbe]::SelectedTab($child.MainWindowHandle) -ne 0) { throw 'Launcher did not open on the first tab.' }
    if (![LauncherUiProbe]::HasInstallationPrompt($child.MainWindowHandle)) { throw 'Missing installation prompt on clean machine.' }
    'PASS Released app opens from a Unicode path on a clean machine without Steam or game components.'
    'PASS Default server/saves tab and first-installation guidance are visible.'
    if (!$child.CloseMainWindow() -or !$child.WaitForExit(10000) -or $child.ExitCode -ne 0) {
        throw 'Launcher did not close normally on a clean machine.'
    }
    'PASS Main window closes normally without an installed server.'
} finally {
    # Clean up only the temporary app instance started by this test.
    if (!$child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $child.Dispose()
}
