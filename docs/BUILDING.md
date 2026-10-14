# 编译与验证

## 简便编译

在 64 位 Windows 上运行 `build.cmd`，或在项目目录运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。

脚本调用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，编译显式列出的十个 C# 文件，并将图标作为 Windows 图标及 `CoopLauncher.AppIcon.ico` 资源嵌入。只引用 .NET Framework 自带的 Windows Forms、Drawing、Web Extensions 与 Visual Basic 等程序集。

默认输出：`bin\Release\BannerlordCoopLauncher.exe`。也可指定输出目录：

```powershell
.\build.ps1 -OutputDirectory C:\Build\BannerlordCoopLauncher
```

脚本可在 Windows PowerShell 5.1 和 PowerShell 7 下使用。若缺少 Framework 编译器，请使用 Visual Studio 工程方式。

## Visual Studio

工程目标为 .NET Framework 4.8、Windows Forms、x64。打开 `BannerlordCoopLauncher.sln`，使用已安装的 .NET 桌面开发工作负载及 4.8 targeting pack，选择 `Release | x64`。工程不需要 NuGet 还原。

本机验证以 `build.ps1` 为准；Visual Studio 工程包含相同源码、程序集引用及嵌入图标。编译器版本不同会使二进制文件校验值不同。

## 自动检查

运行 `test.cmd` 或 `test.ps1`。脚本在临时目录中编译程序并调用内置的 `--self-test` 入口，输出 `self-test.txt`。所有写入使用独立模拟目录，安装检测只读；测试中的密码、世界和路径字符串是夹具。控制台检查另启独立模拟进程，验证隐藏时的中文输出、命令响应及窗口恢复，不启动真实服务器。

回收测试使用模拟回收操作，验证文件成组迁移和失败恢复，不调用真实回收站 API。真实回收接口和运行服务器保存不由此脚本执行。

可指定测试输出目录，但应选择较短路径，兼容 .NET Framework 和 Windows 的传统路径长度限制：

```powershell
.\test.ps1 -OutputDirectory C:\Temp\CoopChecks
```

不要把测试输出、编译程序、游戏配置、用户存档或日志提交到源码仓库。

## 运行时依赖

编译不依赖 MOD 文件。运行 Steam 好友加入功能时，`SteamJoinBridge` 从已经适配的 MOD 目录加载 `Steamworks.NET.dll` 与其原生组件；这些文件由 MOD 安装提供，不在源码包中重新分发。

服务器本体和运行环境也由完整的 `DedicatedServer` 安装提供。助手为服务器子进程设置本机数据目录，不改系统级环境变量或加速器配置。
