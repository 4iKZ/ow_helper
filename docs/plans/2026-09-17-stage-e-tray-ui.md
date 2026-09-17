# Stage E 实施计划（OwHelper.Tray）

> 设计：`docs/specs/2026-09-17-tray-ui-design.md`
> 规则：每个提交可构建、测试全绿、独立推送。

## E1 — 抽取 OwHelper.App 共享库（纯搬迁）

- `git mv` 以下文件到 `src/OwHelper.App/`：`Session.cs` `SessionState.cs` `AppConfig.cs` `AppLog.cs`
  `RuntimeStateStore.cs` `RuntimeRecovery.cs` `SingleInstance.cs`；迁出类型改为 `public`。
- 新建 `src/OwHelper.App/OwHelper.App.csproj`（net8.0-windows、Nullable、TreatWarningsAsErrors、引用 Core）。
- `src/OwHelper/OwHelper.csproj`：引用 OwHelper.App；移除不再需要的 InternalsVisibleTo。
- `tests/OwHelper.App.Tests`：显式引用 OwHelper.App。
- 架构测试：`OwHelper.App` 加入"无 P/Invoke"名单；新增"Core 不引用 App"断言。
- 验证：`dotnet build`（0 警告）、108 测试全绿。

## E2 — 托盘骨架

- 新建 `src/OwHelper.Tray/`（WinForms，`UseWindowsForms=true`，net8.0-windows）。
- `TrayApplicationContext`：NotifyIcon + ContextMenuStrip（占位命令）+ 单实例互斥体（与控制台同名）+ 退出路径。
- 验证：手工启动可见图标，退出无残留；`dotnet build` 全绿。

## E3 — TrayController 与状态映射

- `TrayController`：持有 Session，封装 Start/Stop/ToggleOffscreen/Reattach/OpenLogs/OpenConfig/Exit；
  1 秒 `System.Windows.Forms.Timer` 刷新状态快照并驱动菜单与图标。
- `TrayStatus (record)` + `TrayStatusMapper`：状态与标志 → 颜色/文案/气泡决策（纯函数）。
- 单测：`TrayStatusMapperTests`（4 状态颜色、部分失败降级为赭黄、停止态文案）。

## E4 — 视觉落地

- `Palette`（上述色值常量）、`ToolStripPaperRenderer`（自绘菜单）、`TrayIconFactory`（双描边脉冲折线，按状态色绘制 16/32px）。
- `StatusForm`：完整状态窗口（等价 S 屏 + 4 按钮），套用色板，1 秒刷新（共享 TrayController 快照）。
- 验证：手工观感确认（可先出 HTML 色板预览页）。

## E5 — 气泡通知与异常路径

- 重连成功 / 资源部分失败 / 连续脉冲失败 → 气泡（去重，避免刷屏）。
- 手工演练：用 `OwHelperStandIn` 替身进程验证断链重连气泡。

## E6 — 工程收尾

- CI（自动包含新项目）、`scripts/publish.ps1` 增加托盘发布、README 增加托盘章节、
  架构测试把 `OwHelper.Tray` 纳入红线。
- 验证：CI 绿；publish 产物含 `OwHelper.Tray.exe`。

---

## 执行状态（更新）

- **E0/E1 完成**：设计文档、实施计划、`OwHelper.App` 共享库抽取（110 测试回归通过）。
- **E2–E4 合并为一个提交完成**：托盘应用（暖纸色板、自绘菜单、完整状态窗口、双描边脉冲图标、共享单实例）；
  合并原因：骨架/控制器/视觉一次成型，避免先写一遍再重构。
- **E5 完成**：Session 通知队列 + 托盘气泡（重连、部分失败、连续失败、目标失联）。
- **E6 完成**：publish 脚本含托盘、README 托盘章节、架构测试纳入 `OwHelper.Tray`。
- **测试规模**：78（Core）+ 49（App）= 127。
- **待人工验收**：托盘交互（菜单/状态窗/气泡/退出恢复）、控制台与托盘互斥、图标观感。

> 后续：Stage H 已将托盘前端的 `StatusForm` 并入「大主窗口 MainForm」，本文件的状态窗描述为当时记录。
