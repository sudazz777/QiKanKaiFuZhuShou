# 实现说明

## 界面和安装适配

程序为 .NET Framework Windows Forms 应用。`CoopLauncher` 的主界面与安装、存档、保存流程通过 partial class 组织。路径由 `HostInstallation` 读取 Steam 安装记录、游戏清单及多个 Steam 库取得，或由用户手动选择。

本机安装记录位于当前用户的 `LocalAppData\BannerlordCoopLauncher\installation.json`，包括机器标识校验。它不随 EXE 迁移；新电脑或失效路径会重新适配。`HostBootstrap` 检查必需组件和游戏/服务器版本，创建缺失的本机配置和初始世界模板，保留现有数据。

## 服务器与配置

启动 `BannerlordCoopServer.exe` 时使用 `--data-dir` 指定服务器数据目录，并仅对子进程设置 `BANNERLORD_USER_DIR` 和 `COOP_DATA_DIR`。默认助手配置使用 UDP 4200。

`SaveServerSettings` 备份并更新服务器配置和 MOD 玩法配置。JSONC 的注释、无关字段及已有值尽量原样保留；必需字段不存在或重复时拒绝写入。客户端 `coop_options.json` 不属于服务器设置，v1.6.4 不访问该文件。

本机存档使用 Windows 当前用户的“文档”位置：

```text
文档/Mount and Blade II Bannerlord/CoopData/DedicatedServer/Game Saves
```

玩法配置位于同一用户目录中的 `CoopData/mod-config.json`。

## 内嵌服务器控制台

`ServerConsole` 在助手内显示服务器原生控制台最近 120 行输出，使用 `AttachConsole` 和 `ReadConsoleOutputW` 读取缓冲区，并跳过宽字符的尾单元。启动指定 `WindowStyle.Hidden`，保留控制台，避免 `CreateNoWindow` 或输入重定向破坏已有 `WriteConsoleInput` 保存与停止流程。每次原生操作后释放句柄并脱离服务器控制台。

服务器协议及交互提示保留在原输出中，用户自行阅读并输入；助手不自动接受协议。控制台可按需恢复显示。`LauncherShutdown` 处理关闭：取消自动重启，等待当前保存/导出，发送一次 `stop`，持续检查服务器退出与 UDP 端口释放，再结束助手。关闭过程保持窗口及计时器运行；停服超时保留助手并恢复操作，不强制结束服务器。助手保留自己启动的进程引用，避免进程路径检测暂时失败时漏掉后台服务器。读取失败时回退展示服务器日志文件并提供独立窗口入口。

隔离检查启动助手自身的控制台模拟模式，验证中文输出、单行命令响应、窗口隐藏/恢复及正常退出，不依赖或启动真实服务器。

## Steam

专用服务器负责 Steam GameServer 登录与创建大厅；助手从当前服务器日志取得大厅编号。`SteamJoinBridge` 通过已安装 MOD 的 Steamworks 包装器异步检查大厅所有者和必要元数据，再发布当前账号的 Rich Presence 加入信息。

Steam 客户端的好友入口、大厅创建与 P2P 战役连接分别处理，不能仅凭大厅可见认定连接成功。`SteamRuntime` 对失效 Steam PID 记录进行当前用户、当前会话、程序路径等核验，备份后才写入；有效或不确定的记录保留，失败作为提示继续处理。

## 战役文件

每个战役以共同名称关联 `.sav` 世界、`.json` 联机角色记录，以及存在的 `.backup1` / `.backup2` 两组文件。内部模板 `default_new_game.sav` 和交换文件不作为普通战役删除。

`SaveStorage` 实现成组导出、改名和删除，防止覆盖相似名称或目录外文件。改名保持文件内容；只更新匹配的开服存档名称。删除先成组暂存，再移入 Windows 回收站；配置或回收失败时恢复原件，不降级为永久删除。运行中的战役须先停止再改名或删除。

## 立刻保存

通过 Windows 控制台输入向当前服务器发送 `save`。`InstantSave` 捕获进程、战役、日志位置与文件状态，只认可本次请求之后对应世界的完整保存成功日志，并检查世界及联机角色 JSON 的新写入与战役标识。

“立刻保存”保存当前运行世界，与下拉框选择无关；它不应用房间或难度修改。未完成、占用、旧日志、其他战役及明确失败日志不会被报告为成功。

## 测试与维护

内置 `LauncherChecks` 调用路径、适配、存档和保存确认检查。保持模拟数据与真实运行目录分开。新增 MOD 版本适配前，应重新确认配置字段、日志格式、Steam 元数据和官方服务器启动参数。
