[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $OutputDirectory
New-Item -ItemType Directory -Path $root -Force | Out-Null
$original = $Executable
$fixture = Join-Path $root 'ConsoleSubsystemFixture.exe'
$bytes = [IO.File]::ReadAllBytes($original)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
$subsystemOffset = $peOffset + 24 + 68
if ([BitConverter]::ToUInt16($bytes, $subsystemOffset) -ne 2) {
  throw 'Expected the product executable to use the Windows GUI subsystem.'
}
# Change only a throwaway copy into a real console application. The
# product executable and downloadable package remain untouched.
[BitConverter]::GetBytes([UInt16]3).CopyTo($bytes, $subsystemOffset)
[IO.File]::WriteAllBytes($fixture, $bytes)
if ([BitConverter]::ToUInt16([IO.File]::ReadAllBytes($fixture), $subsystemOffset) -ne 3) {
  throw 'Fixture must use the console subsystem.'
}
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
public static class WindowlessCheckInvoker {
  [DllImport("kernel32.dll")] static extern bool FreeConsole();
  [DllImport("kernel32.dll")] static extern bool AttachConsole(uint pid);
  [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
  static object Call(Type type, string name, params object[] args) {
    return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, args);
  }
  static void WaitFor(Type session, Process process, string expected) {
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline) {
      object[] args = new object[] { (uint)process.Id, null };
      if ((bool)Call(session, "TryRead", args) && ((string)args[1]).Contains(expected)) return;
      if (process.HasExited) break;
      Thread.Sleep(50);
    }
    throw new Exception("Missing pipe output: " + expected);
  }
  public static string[] Run(string executable, string report) {
    var assembly = Assembly.LoadFrom(executable);
    var session = assembly.GetType("ServerPipeSession", true);
    var input = assembly.GetType("ConsoleInput", true);
    var marker = report + ".saved";
    var start = new ProcessStartInfo(executable, "--pipe-fixture \"" + marker + "\"") {
      UseShellExecute = false, CreateNoWindow = true,
      RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
      StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
    };
    var results = new List<string>();
    using (var child = Process.Start(start)) {
      uint pid = (uint)child.Id;
      Call(session, "Register", child);
      try {
        WaitFor(session, child, "PIPE \u4e2d\u6587\u65e5\u5fd7\uff0c\u8bf7\u8f93\u5165\u547d\u4ee4\uff1a");
        WaitFor(session, child, "STDERR ");
        FreeConsole(); bool attached = AttachConsole(pid);
        bool visible = false;
        try { visible = attached && IsWindowVisible(GetConsoleWindow()); }
        finally { if (attached) FreeConsole(); }
        if (visible) throw new Exception("CREATE_NO_WINDOW created a visible console window.");
        results.Add("PASS Console-subsystem program has no visible console window and streams partial prompts/stdout/stderr.");
        Call(input, "Send", pid, "\u4e2d\u6587\u547d\u4ee4");
        WaitFor(session, child, "COMMAND \u4e2d\u6587\u547d\u4ee4");
        results.Add("PASS Console-subsystem program receives UTF-8 commands through the production pipe session.");
        Call(input, "Send", pid, "stop");
        if (!child.WaitForExit(10000) || child.ExitCode != 0 || !File.Exists(marker) || File.ReadAllText(marker) != "saved")
          throw new Exception("Console-subsystem program did not save and exit.");
        results.Add("PASS Console-subsystem program saves before exiting on stop.");
      } finally {
        if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
        Call(session, "Remove", pid);
      }
    }
    return results.ToArray();
  }
}
'@
$report = Join-Path $root 'report.txt'
$results = [WindowlessCheckInvoker]::Run($fixture, $report)
$results | ForEach-Object { Write-Output $_ }
if ($results.Length -ne 3) { throw 'Windowless checks did not complete.' }
