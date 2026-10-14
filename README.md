# 骑砍2开服助手

用于 Windows 上《骑马与砍杀2》Bannerlord Co-op MOD 专用服务器的中文开服助手。当前版本为 **v1.6.4**，源码整理日期为 **2026-10-14**。

[⬇ 下载 Windows 64 位成品 v1.6.4](https://github.com/sudazz777/QiKanKaiFuZhuShou/releases/download/v1.6.4/BannerlordCoopLauncher-v1.6.4-win-x64.zip)

下载后“全部解压”，双击“骑砍2开服助手.exe”。成品包包含程序及使用说明。

本项目提供开服与存档管理界面，使用本机已经安装的游戏、MOD 和专用服务器组件。Bannerlord Co-op 是联机 MOD 本体；Bannerlord Coop 简体中文补丁是汉化补丁。

## 功能

- 自动检测 Steam、多盘游戏库、游戏和专用服务器位置，支持首次适配与手动选择安装目录。
- 启动服务器、保存并重启、保存并停止，以及打开游戏。
- 公开 Steam 大厅状态显示，并尝试发布 Steam 好友资料页加入入口。
- 新建或导入战役、立刻保存当前进度、完整导出到桌面。
- 存档改名时同步世界、联机角色记录和两组历史备份。
- 从服务器目录删除存档，并整组移入 Windows 回收站。
- 设置、修改或取消房间密码，以及自动保存间隔。
- 总难度“冒险家、勇士、霸主”，独立难度和生育死亡等选项。
- 保存设置前备份，保留已有存档与其他配置字段。

## 运行条件

64 位 Windows 与 .NET Framework 4.x。使用服务器功能还需要本机安装 Steam、游戏及匹配版本的 Bannerlord Co-op，专用服务器的 `DedicatedServer` 文件夹必须完整。

当前实现适配并验证过 Coop 0.1.6 / 游戏 1.4.8 的安装与配置格式。其他版本会接受安装检查；新增或改变的必需字段可能需要继续适配。

助手不附带游戏、MOD、服务器、Steam SDK DLL 或用户存档。Steam 联机依赖 Steam 在线、版本匹配及可用网络；公开大厅可见与玩家实际进入战役是两个独立状态。

## 从源码生成程序

在 Windows 上双击 `build.cmd`。也可以在 PowerShell 中运行：

```powershell
.\build.ps1
```

生成程序为 `bin\Release\BannerlordCoopLauncher.exe`。编译脚本使用 Windows 上已安装的 .NET Framework C# 编译器，无需下载 NuGet 包。

使用 Visual Studio 时打开 `BannerlordCoopLauncher.sln`，安装 .NET 桌面开发工作负载和 .NET Framework 4.8 targeting pack，选择 `Release | x64` 编译。两种方式的行为代码和图标来自同一组源码。

详细说明见 [编译与验证](docs/BUILDING.md)；用户操作说明见 [使用说明](docs/使用说明.txt)。

## 验证

双击 `test.cmd`，或运行：

```powershell
.\test.ps1
```

脚本先编译，再运行程序内置的配置、存档、路径和保存确认测试。模拟数据和结果默认保存于 Windows 临时目录的独立文件夹。测试只读检测当前电脑安装，不启动真实服务器，也不修改真实存档或调用真实回收站删除。

本源码整理包已重新编译并通过 30 项检查。验证范围见 [验证记录](docs/VERIFICATION.md)。

## v1.6.4 修复

旧版助手在启动前写入客户端 `coop_options.json` 的 `lastServerAddress`。新电脑没有这个可选字段时会出现“配置字段缺失或重复”错误。当前版本取消这项客户端历史地址写入，服务器设置仍严格验证。

本机直连在游戏内填写 `127.0.0.1`，端口 `4200`。房间有密码时填写实际密码，没有密码则留空。好友资料页加入需要助手保持运行及 Steam 加入入口就绪；关闭助手后服务器可继续运行。

## 源码结构

| 文件 | 用途 |
| --- | --- |
| `src/CoopLauncherV2.cs` | 程序入口、主界面、服务器启停、配置编辑及综合检查 |
| `src/InstallationPage.cs` | 本机安装设置界面与首次适配流程 |
| `src/HostInstallation.cs` | Steam 库、游戏与服务器路径检测，本机安装记录 |
| `src/HostBootstrap.cs` | 安装完整性与版本检查，初始化缺失配置和模板 |
| `src/SteamJoinBridge.cs` | 使用已安装 MOD 的 Steamworks 包装器发布好友加入入口 |
| `src/SteamRuntime.cs` | 校验 Steam 当前用户进程记录，必要时备份后修正失效 PID |
| `src/SaveStorage.cs` | 成组导出、改名、回收和失败恢复 |
| `src/SaveManagement.cs` | 存档改名与删除的界面流程 |
| `src/InstantSave.cs` | 发送保存指令，核对本次日志、世界与角色文件写入 |
| `assets/bannerlord-knight.ico` | 嵌入程序的骑士图标 |

更多实现说明见 [架构说明](docs/ARCHITECTURE.md)。`SOURCE_MANIFEST.json` 记录核心源码和图标的 SHA-256，便于核对整理包与最新版源文件一致。

## 上传 GitHub

把此文件夹中的源码、工程文件、脚本和文档提交到仓库即可。`.gitignore` 已排除编译产物、个人路径设置、服务器配置、日志、战役及测试输出。可运行程序适合另放在 GitHub Releases。

本源码包尚未替项目选择开源许可证；需要许可证时可在仓库中添加。图标来源见 [资源说明](assets/README.md)。

## 相关项目

- [Bannerlord-Coop-Team / BannerlordCoop](https://github.com/Bannerlord-Coop-Team/BannerlordCoop)
- [官方专用服务器说明](https://github.com/Bannerlord-Coop-Team/BannerlordCoop/blob/development/deploy/Server%20Instructions.md)
