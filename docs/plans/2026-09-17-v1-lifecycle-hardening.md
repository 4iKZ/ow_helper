# OW Helper v1 实施计划（Lifecycle Hardening）

> 依据：`docs/ow_helper_PRD_v1.0.md`（v1.0）+ 评审后的顺序调整。
> 基线提交：`8938d85`。规则：**每个提交可构建、测试全绿、独立推送**；行为契约（PRD §9.1）不变。

## 阶段总览

| Stage | 主题 | 提交 | 关键产出 |
|---|---|---|---|
| A | 工程卫生 | A1, A2 | TFM/文档/README/Nullable |
| B | Core 硬化 | B1–B4 | Native 结果化、Governor v2、TargetIdentity、Placement v2 |
| C | Session 生命周期 | C1, C2 | 状态机、串行化、Reattach 语义、Session 测试 |
| G1 | 实机闸门 | — | 验收矩阵 D/E/F + ENV-02 最小化 + 4h soak |
| D | 产品化 | D1–D6 | extended key、日志、config、单实例+崩溃恢复、NVIDIA、CI |
| E/F | 可选 | E, F | 托盘 UI；NVAPI/Job 实验（默认关） |

---

## Stage A — 工程卫生

### A1（本提交）— TFM + 文档修正 + README

- **文件**：4 个 `*.csproj`（`net8.0` → `net8.0-windows`）、`docs/refactor-plan.md`（38→35）、新增 `README.md`、`LICENSE-TODO.md`、本计划。
- **行为**：无变化。
- **验证**：`dotnet build`/`dotnet test` 35/35。
- **回滚**：单提交 revert。

### A2 — Core 启用 Nullable + TreatWarningsAsErrors

- **文件**：`src/OwHelper.Core/*.cs`（标注可空）、Core csproj。
- **决策**：仅 Core 先启用；apps 在 C1 重写时随文件启用；tests 最后。
- **行为**：无变化（纯标注）。
- **验证**：build 0 warning；测试 35/35。

---

## Stage B — Core 硬化

### B1 — Native 结果化

- **文件**：`Native.cs`（强类型 `SetProcessInformation(..., ref PROCESS_POWER_THROTTLING_STATE, size)`，删除 `AllocHGlobal` 路径；命名常量 `PROCESS_POWER_THROTTLING_CURRENT_VERSION=1`、`PROCESS_POWER_THROTTLING_EXECUTION_SPEED=0x1`），新增 `Interop/OperationResult.cs`。
- **行为**：无变化（仅调用形式与错误捕获）。
- **测试**：`OperationResult` 构造/成功语义。

### B2 — ResourceGovernor v2

- **API**：`Apply() → ResourceApplyResult`（逐项 OperationResult）、`Restore() → ResourceRestoreResult`、`Snapshot { PriorityClass, PriorityCaptured, PowerThrottle }`、幂等。
- **语义**：
  - Apply：`PriorityClass = BelowNormal`（先快照原值）；EcoQoS `ControlMask=EXECUTION_SPEED, StateMask=EXECUTION_SPEED`。
  - Restore：恢复**原优先级**；Power 恢复 `ControlMask=0, StateMask=0`（system-managed）。禁止 `catch {}`，禁止虚报成功。
- **测试**（`B2`）：自进程上 capture→apply→restore 断言回到**原值**（不假设 Normal）；mask 纯函数断言；`SetProcessInformation` 失败路径返回失败（对已退出进程句柄调用）；二次 Apply/Restore 幂等。
- **修**：BUG-003/004/005。

### B3 — TargetIdentity + 校验

- **文件**：新增 `Targeting/TargetIdentity.cs`、`GameWindow.Validate() → TargetValidationResult`。
- **规则**（PRD §8.2）：进程未退出 + `process.Id == Pid` + `IsWindow(Hwnd)` + `GetWindowThreadProcessId(Hwnd) == Pid`；建议包含 `ProcessStartTimeUtc`。
- **测试**：正常窗口 Valid；伪造 PID 不匹配 → Invalid（用 FakeWindow + 假身份）；进程退出 → Invalid。
- **修**：BUG-006。

### B4 — WindowPlacement v2

- **状态**：`None / Captured / Offscreen / RestoreFailed / Stale`（以结果结构表达）。
- **规则**：Move 前校验 identity + `GetWindowRect` 成功；`SetWindowPos` 返回值校验 + best-effort 复核；失败不得置 Offscreen；Restore 只对同 PID/HWND 执行；stale 时安全丢弃。
- **测试**：FakeWindow 上 Move 记录矩形→Restore 还原；Move 失败（无效 HWND）不改状态；PID 不匹配拒绝 Restore。
- **修**：BUG-008。

---

## Stage C — Session 生命周期

### C1 — 状态机 + 串行化 + UI 拆分

- **文件**：`SessionState.cs`、`Session.cs`（重写）、`ConsoleUi.cs`、`Program.cs`（瘦身）；Core 新增 4 个测试缝隙接口：`ITargetLocator` / `IPulseSender` / `IResourceGovernor` / `IWindowPlacementController`（**仅此 4 个**）。
- **决策**：UI 与 Session 分离；所有状态变更经 `SemaphoreSlim(1,1)`；`Start/Stop/Reattach/DisposeAsync` 为异步，Governor/Placement 保持同步结果结构。
- **测试**（新项目 `tests/OwHelper.App.Tests`，`InternalsVisibleTo`）：S01 Ready→Running（Apply 一次）；S02 Stop（cancel+restore+Ready）；S03 Start 二次 no-op；S04 Stop 二次幂等；S10 Dispose from Running 仅一次清理。

### C2 — Reattach 语义

- **规则**：目标失效 → `Reattaching`（1s 首延迟 / 2s 轮询；Stop 立即取消）；新目标 → 重建 TargetIdentity → **capture + Apply** → Running；手工 `r`（Running 中）→ 暂停脉冲 → 恢复旧 placement/governor → detach → 重挂；**任何 Reattach 前先恢复旧 placement，绝不丢快照**；日志记录旧/新 PID。
- **测试**：S05 目标死亡自动重挂并 reapply；S06 前台跳过但调度继续；S07 offscreen 后手工 r 不丢快照；S08 连续 3 次失败且 identity 无效 → Reattach；S09 Priority 失败 + EcoQoS 成功 → Running + degraded。
- **修**：BUG-001/002。

### G1 — 实机验收闸门（需用户配合）

- PRD §24 矩阵 A–H（A 后台输入 10/10、B 前台跳过、C 恢复后台、D OW 重启自愈、E offscreen 可恢复、F 优先级恢复原值、G EcoQoS 语义、H GPU 限帧）；
- 追加 ENV-02 最小化验证；4 小时 soak（PRD §30 Reliability）。

---

## Stage D — 产品化

- **D1** extended key：`KeySpec(VirtualKey, IsExtended, DisplayName)` + lParam bit 24 + 测试（方向键/右 Ctrl/右 Alt/Insert…NumPad）。
- **D2** 结构化日志：`%LOCALAPPDATA%\OwHelper\logs\owhelper-YYYYMMDD.log`，事件表见 PRD §16.2；除 best-effort 清理外禁止空 catch；`S` 状态屏 + 版本号。
- **D3** `config.json`：`%LOCALAPPDATA%\OwHelper\config.json`；容错规则见 PRD §15.3；**jitter 默认关闭**（如保留仅作调度抖动，命名不得含规避语义）。
- **D4** 单实例（命名互斥体）**先行**，其后 `runtime-state.json` 崩溃恢复 + 启动提示（identity 一致才提示恢复；不一致删除 stale）。
- **D5** NVIDIA 检测 + 文案引导（不自动写驱动 profile；NVAPI 仅放入 Stage F 研究）。
- **D6** CI（windows-latest: restore/build/test）+ `scripts/publish.ps1`（win-x64, framework-dependent）+ README 完整化 + 源码扫描 gate（`WriteProcessMemory`/`ReadProcessMemory`/`VirtualAllocEx`/`CreateRemoteThread`/`SetWindowsHookEx` 等）+ License 决策落地。

## Stage E / F — 可选

- **E** `OwHelper.Tray`（WinForms NotifyIcon）复用 Session，不复制 Core 逻辑；状态色见 PRD §19。
- **F** NVAPI DRS spike / Job Object CPU cap 实验：默认关闭，需重新架构审查后才可进入默认路径。

---

## 风险与决策项

| 项 | 说明 | 处理 |
|---|---|---|
| 实机验收依赖 | G1 需要用户开着 OW 执行（含 4h soak） | 到点通知用户；最小化结论可能影响 offscreen 定位 |
| License | 未选定 | 用户决策，LICENSE-TODO 占位 |
| Nullable 顺序 | 先 Core 后 apps | apps 在 C1 随重写启用 |
| Jitter 默认值 | 现行为 ±15% | D3 改为默认关闭（PRD §9.5），属有意行为变更 |
| G1 前使用注意 | `r` 不可在 offscreen 时按；OW 重启后需停/开恢复节流 | README 已注明 |

---

## 执行状态（更新）

- **Stage A / B / C / D：已完成**（A1–D6，逐提交构建 + 测试 + 推送）。
- **G1 自动化部分已完成**：重启自愈、移出+手动重挂恢复窗口、优先级往返（AboveNormal→BelowNormal→AboveNormal）
  由 `tests/OwHelper.App.Tests/LiveSessionIntegrationTests.cs` 使用替身窗口进程常驻验证，不需要游戏。
- **G1 手工项待用户执行**：A/B/C（后台脉冲生效 + 前台跳过）、I（最小化状态）、J（4 小时 soak）。
- **Stage E/F 未开始**：托盘 UI、NVAPI/Job Object 实验（按计划为可选，默认关闭）。
- 测试规模：75（Core）+ 33（App）= 108，含架构红线（无 P/Invoke 泄漏、无 UI 框架依赖、源码禁用 API 扫描）。
