# Stage H 实施计划（前端大众化）

> 设计：`docs/specs/2026-09-17-friendly-ui-design.md`
> 规则：每个提交可构建、测试全绿、独立推送。

## H1 — 项目更名 OwHelper.Tray → OwHelper.Desktop

- `git mv` 项目目录与 csproj；`AssemblyName`/`RootNamespace` = `OwHelper.Desktop`；源码 `namespace OwHelper.Desktop`。
- 同步：`ow_helper.sln`、`scripts/publish.ps1`、README 路径与 exe 名、测试项目引用、架构测试名单（`OwHelper.Desktop`）。
- 不变：类名、单实例互斥体名、托盘图标、行为。
- 验证：161 测试全绿、Release 构建 0 警告。

## H2 — PlainLanguage 文案层

- 新增 `OwHelper.Desktop/PlainLanguage.cs`：`ConnectionText(TrayStatus)`、`RunText`、`NoticeText(SessionState/事件)`、`ErrorMessage(Win32)` 等纯函数。
- `TrayStatusMapper` 改为委托给 PlainLanguage（保留颜色映射）。
- 测试：`PlainLanguageTests`（断言不含 `PID`/`HWND`/`脉冲`/`EcoQoS` 等技术词；关键场景文案正确）。

## H3 — MainForm 主窗口

- 新 `MainForm.cs`：状态卡（连接/运行/已按键/倒计时）、大号一键按钮、停止/隐藏按钮、常驻四步指引、高级设置折叠区。
- `DesktopApplicationContext`（原 TrayApplicationContext）：主窗口为默认入口；托盘菜单精简；关窗=隐藏（首次气泡）。
- 状态刷新复用 1 秒 Timer + `TrayController.Snapshot()`；倒计时由 `LastPulseAt + IntervalSec` 计算。

## H4 — QuickPresets 托比昂一键

- `QuickPresets.cs`：`TorbjornPass`（shift/30s/hold200/skip-foreground/jitter0）+ `Apply(AppConfig)`。
- 主窗口按钮：应用 → `ApplySettingsAsync` → `StartAsync` → 状态卡与按钮文案切换。
- 测试：`QuickPresetsTests`。

## H5 — 关窗行为与设置文案

- 主窗口 `FormClosing`：非退出时取消关闭并 `Hide()`；首次提示气泡。
- `SettingsForm` 文案按术语映射改写；标题「更多设置」；从主窗口「自定义按键…」「更多设置…」进入（同一窗口，定位到对应分组）。

## H6 — 收尾

- README 面向普通用户重写（快速开始 4 步、常见问题、术语对照简表）；spec/plan 状态同步。
- 手工验收清单：一键启动 30s/Shift、关窗最小化、托盘唤回、隐藏游戏窗口、停止恢复资源。

---

## 执行状态（更新）

- **H1 完成**：项目更名 `OwHelper.Desktop`（程序集/命名空间/exe；sln、publish、README、测试与架构红线同步）。
- **H2 完成**：`SessionNotice` 结构化通知 + `PlainLanguage` 文案层（15 个测试断言"无技术词"）。
- **H3 完成**：`MainForm` 主窗口（状态卡/大按钮/常驻四步指引/更多设置入口），删除旧 StatusForm；
  关闭主窗口=最小化到托盘（H5 加首次气泡）。
- **H4 完成**：`QuickPresets`「托比昂战令」= Shift / 30 秒 / 按住 200ms / 你玩游戏时暂停（单测钉死默认值）；
  大按钮点击=应用预设→保存→立即开始。
- **H5 完成**：关窗气泡提示；设置界面全部改人话（下拉项本地化：后台优先级别/记录详细程度）。
- **H6**：README 重写完成；Release 构建 + 实机验收待执行。
- 测试规模：104（Core）+ 78（App）= **182**。
- 待人工验收：一键启动后 OW 每 30 秒按一次 Shift、关窗最小化与托盘唤回、隐藏/显示游戏窗口、停止后资源恢复。
