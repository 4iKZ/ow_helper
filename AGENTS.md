# AGENTS.md — OW 助手（ow_helper）

## 定位

Windows 后台输入调度器：让《守望先锋》在后台自动按设定按键，配合进程资源节流与"老板键"（隐藏游戏窗口并移出任务栏）。
只使用 Win32 窗口消息与公开调度 API；**禁止**注入 / Hook / 内存读写 / 驱动级模拟 / 反作弊规避（有源码扫描测试强制）。

## 命令

- 构建：`dotnet build ow_helper.sln -c Release`（必须 0 警告；Core/App/Desktop 已开 TreatWarningsAsErrors）
- 测试：`dotnet test ow_helper.sln -c Release`（193 项：单元 + 假窗口 + 实机替身集成 + 架构红线）
- 桌面版：`src\OwHelper.Desktop\bin\Release\net8.0-windows\OwHelper.Desktop.exe`
- 控制台版：`src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe [按键,逗号分隔] [间隔秒]`
- 发布：`scripts\publish.ps1` → `artifacts\OwHelper-win-x64`；安装包：`installer\OwHelper.iss`（ISCC 编译，需自包含发布目录）；诊断实验：`BgKeyProbe.exe`
- 用户数据：`%LOCALAPPDATA%\OwHelper\`（config.json / runtime-state.json / logs\）

## 技术栈

.NET 8（net8.0-windows）· WinForms（仅桌面前端）· xUnit · GitHub Actions（windows-latest）

## 目录与约定

- `src/OwHelper.Core`：**唯一 P/Invoke 层**（Win32、目标发现/校验、脉冲、资源、窗口样式、GPU 检测）。app 程序集不得出现 `DllImport`（架构测试强制）。
- `src/OwHelper.App`：`Session` 状态机（运行期状态唯一所有者）、配置、日志、崩溃恢复、`AppStartup`（前端共用启动序列）、`Session.ApplyConfig`（配置→会话唯一映射点；新增设置项只改 `AppConfig` + `ApplyConfig`）。
- 前端：`OwHelper.Desktop`（面向普通用户，文案走 `PlainLanguage`，不要引入技术词）/ `OwHelper`（技术控制台，保留术语）。
- 测试：`tests/OwHelper.Core.Tests`（单元 + 架构红线）、`tests/OwHelper.App.Tests`（会话生命周期 + 实机替身）、`TestSupport*`（FakeWindow / 替身进程）。
- 约定：每个提交可构建、测试全绿、独立推送；**行为契约不得随手改**——脉冲序列 `SETFOCUS→按键→KILLFOCUS`、老板键语义、控制台文案与退出码、日志事件名、config/runtime-state 文件格式。
- 文档：`docs/specs`、`docs/plans` 是历史设计记录；**现役说明以 README 为准**。

## 当前状态与下一步

- 版本 1.1.2（MIT；GitHub Release 双包 + 安装包 + SHA256）；全新 Windows 11 现代精致 UI（原生双缓冲抗锯齿圆角按钮、微软雅黑排版、2x2 KPI 核心数据卡片与字号自适应、平滑倒计时进度条与扁平战术图标）。
- 发布：发版前改 `Directory.Build.props` 的 `Version`，推送 `tag v*` 后 `release.yml` 自动构建双包、安装包并建 Release。
- 待办：① 4 小时 soak 与"游戏最小化"实测（用户侧，含 NoActivate 真实前台行为）② `ResourceGovernor.Snapshot` 公开面去留（`docs/plans/2026-09-18-architecture-review.md` 发现 9）。
