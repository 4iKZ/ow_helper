# v2.0 Release Readiness 修复计划（R1）

> **给执行者**：按 Task 顺序逐项执行；每个 Task 都是「先写失败测试 → 跑失败 → 最小实现 → 跑通过 → 提交」。
> 全程遵守 `AGENTS.md`：`dotnet build ow_helper.sln -c Release` 必须 0 警告，`dotnet test ow_helper.sln -c Release` 全绿，行为契约（脉冲序列 / 老板键语义 / 控制台文案与退出码 / 日志事件名 / 配置文件格式）改动需在提交信息里写明。
> 计划基于 `master @ 5b6d154`（PRD 基线 2bb0437 + 两个纯文档提交；代码未变，结论一致）。

**目标**：把 PRD v2.0 的 P0-01…P0-06 全部修复并补齐失败路径测试，再做选定 P1；实现"用户配置按预期生效、窗口状态可重试恢复、各入口语义一致、Win32 消息参数正确、后台不抢焦点"。

**架构约束**：保持 `Core ← App ← Frontends`；Session 仍是运行期状态唯一 Owner；P/Invoke 只进 Core；不注入 / 不 Hook / 不驱动（架构红线测试强制）。

**验证命令**（每个 Task 结尾跑一遍）：
```powershell
dotnet build ow_helper.sln -c Release
dotnet test ow_helper.sln -c Release
```

---

## Part A. 逐条复核（PRD 主张 vs 实际代码）

| PRD 项 | 判定 | 证据 |
|---|---|---|
| P0-01 主按钮覆盖用户设置 | **属实** | `MainForm.ToggleRunAsync` 先 `QuickPresets.Apply` 再 `ApplySettingsAsync`（`MainForm.cs:349-350`）；托盘"开始挂机"只调 `StartAsync()`（`TrayApplicationContext.cs:34-39`）→ 两套语义。连带问题：按钮文案与副标题也硬编码托比昂（`MainForm.cs:373,378`），改完设置后界面仍在撒谎 |
| P0-02 立即/下次生效语义不一致 | **属实** | `Session.ApplyConfig` 无 gate、直接换 recipe/interval/jitter/policy（`Session.cs:85-95`）；Loop 的 `Task.Delay` 不会被打断（`Session.cs:325-327` 只有 `wakeCts`，仅 Reattach 用）；UI 文案仍说"下次「开始」生效"（`SettingsForm.cs:135`、`TrayController.cs:36,42`） |
| P0-03 恢复失败丢 pending | **属实** | `TaskbarHidden = false` 在检查 `RestoreStyle` 结果之前（`WindowPlacement.cs:134`）；`IsOffscreen => State == Offscreen`（:39），RestoreFailed 后不再重试；MoveOffscreen 回滚失败时忽略 `RestoreStyle` 返回值并清 `TaskbarHidden`（:90-94） |
| P0-04 部分恢复仍删恢复文件 | **属实** | `if (result.Success) store.Clear();` 只看位置（`RuntimeRecovery.cs:49`），样式失败只打印（:43-47），且返回值也只看位置 |
| P0-05 鼠标 wParam 语义错误 | **属实** | DOWN/UP 都带 `MK_LBUTTON`（`MouseInput.cs:45-56`）；XBUTTON LOWORD 恒 0（:61）；无跨键状态聚合（chord / Shift+鼠标）。现有测试固化了错误行为：`MouseInputTests.cs:68` 断言 UP 的 wParam=0x0001 |
| P0-06 内部恢复抢焦点 | **属实** | `EnsureShown` 用 `SW_RESTORE`（`WindowStyle.cs:12`），被 MoveOffscreen 前置（`WindowPlacement.cs:73`）与 Restore（:121）调用；RuntimeRecovery 不 un-minimize。无 NoActivate 通道 |
| P1-01 恢复原始 Power Throttling | 属实但低价值 | `ResourceGovernor.Restore` 固定回 system-managed（`ResourceGovernor.cs:40-41`）；`Native` 无 `GetProcessInformation`（需新增 P/Invoke） |
| P1-02 多同名进程只取第一个 | **属实** | `GameWindow.Find → FirstProcess(...)` 取 `processes[0]`，其余不 Dispose（`GameWindow.cs:39-62`） |
| P1-03 配置写入原子化 | **属实**；迁移框架部分见 Part B | `AppConfig.Save` 直接 `File.WriteAllText`（`AppConfig.cs:100-105`）；无 schemaVersion；损坏时用默认值但不告知 UI（problems 只进日志，`AppStartup.cs:24-27`） |
| P1-04 桌面端 GPU 缺失 + FPS 未校验 | **属实** | 桌面端无 GPU 指引（`TrayController` 已无 GPU 字段——上一轮按死代码删除）；`Validate()` 未校验 `GpuBackgroundFpsTarget`（`AppConfig.cs:133-193`） |
| P1-05 NVAPI 自动化 | 采纳其结论：**R1 不做** | 与仓库"不做驱动/注入"红线和成本都相符 |
| P1-06 Desktop 不显示配置问题 | **属实** | `AppStartupResult.Problems` 在 Desktop 未被消费（`TrayApplicationContext.cs:27-30`）；控制台已打印（`OwHelper/Program.cs:48-51`） |
| P1-07 互斥体字面量硬编码 | **属实** | `Desktop/Program.cs:14` vs `AppStartup.SingleInstanceName`（控制台已用常量） |
| P1-08 次数是"本次"还是"累计" | **属实** | `StartAsync` 不重置 `pulseCount`（`Session.cs:145-178`），UI 固定显示"已自动按键 N 次"（`PlainLanguage.PulseSummary`） |
| P1-09 日志事件名混乱 | **部分属实** | 属实点：恢复消息记成 `CONFIG_LOAD_FAILED` + `operation=recovery`（`AppStartup.cs:34`）；`RESOURCE_RESTORE` 部分失败没有 `_PARTIAL` 变体（`Session.cs:489-497`）；配置保存记成 `CONFIG_APPLIED`（`TrayController.cs:33`）。其余事件名已基本自洽，PRD 列的清单多数已存在 |
| P1-10 发布工程未闭环 | **属实** | 无 LICENSE（仅 `LICENSE-TODO.md`）、无版本号、无 Releases、CI 无 publish |
| P1-11 CI 无发布路径验证 | **属实** | `.github/workflows/ci.yml` 只有 restore/build/test |
| P1-12 目标进程句柄生命周期 | **属实** | Session 不 Dispose 选中进程；reattach 只 Release governor（`Session.cs:386-400`），旧 GameWindow/Process 泄漏；`FirstProcess` 丢弃的进程对象不 Dispose |
| §27.4 连续失败即时校验 | **属实缺陷** | `failureStreak==3` 只提示（`Session.cs:314-319`），不做 Validate / 不等 interval 直接 Reattach |
| §59 wakeCts 生命周期 | **属实** | 每轮 new，不 Dispose（`Session.cs:325`） |
| P2-01 Profile / P2-02 诊断页 / P2-03 支持包 / P2-06 资源观测 / §62 docs 重组 / §63 ADR | 见 Part B：**建议 R1 不做** | — |
| §40-43 测试策略、§49-50 边界、§68 保护清单 | 采纳 | 与本仓库既有测试/架构红线一致 |

---

## Part B. 与 PRD 的差异（建议裁剪，执行前需 Owner 一句话确认）

1. **§10 `IConfigMigration` 迁移框架 → 不做**。当前只有一种落盘格式，没有 v1→v2 真实迁移对象，先加接口是空转。只加 `schemaVersion` 字段 + 原子写 + 备份回退；等到真有第二版 schema 时再提取迁移点。
2. **T05–T07 的"强制失败"**：真实 Win32 窗口几乎无法制造 `SetWindowPos`/`SetWindowLongPtr` 失败。计划改为：给 `WindowPlacement` 加一个 6 行的**测试缝**（可注入 moveTo/restoreStyle 两个委托，生产默认仍调静态方法），失败路径可精确测试；不搭故障注入框架。
3. **P0-02 不引入 `SessionConfigSnapshot` 公开类型**：Loop 每轮开头一次性读取 4 个字段到局部变量即可满足"一轮一快照"，API 面零增长（控制台 `session.IntervalSec = ...` 也继续可用）。
4. **P2 全部推迟**（P2-01/02/03/05 部分/06）。其中 P2-04 版本显示并入发布阶段；P2-05 只做"Esc 关闭设置窗"一条（`CancelButton` 现在缺失），DPI 可访问性靠现有流式布局 + DIP 缩放，人工核对即可。
5. **§62 docs 目录重组 / §63 ADR → 不做**：`AGENTS.md` + 现有 superseded 标记已覆盖"Agent 读到旧文档"的风险；再建 ADR 目录属于文书工程。
6. **P1-04 GPU 指引的边界**：桌面端只显示"建议在 NVIDIA 控制面板设置后台最大帧率 20 FPS"静态指引（检测到 NVIDIA 才显示），不得显示"已限制为 20 FPS"。
7. **预设与默认值等价**：`QuickPresets.TorbjornPass` 的取值（shift/30s/200ms/跳过前台/jitter 0）与 `AppConfig` 默认值完全一致，且 `SettingsForm` 已有「恢复默认」。因此 Task 1 的"显式预设入口"就是给该按钮改名并显式应用预设，**不新增任何 UI**；`QuickPreset.Apply` 从此只被用户显式触发的路径调用。

---

## Part C. 任务清单

### Task 0：基线确认

- [ ] `git status` 干净、`git log --oneline -1` 为 `5b6d154`
- [ ] `dotnet build ow_helper.sln -c Release` 0 警告；`dotnet test ow_helper.sln -c Release` 193/193

---

### Task 1（P0-01）：启动不再隐式覆盖用户设置

**Files**
- Modify: `src/OwHelper.Desktop/MainForm.cs:341-354`（ToggleRunAsync）、`:373-378`（文案）
- Modify: `src/OwHelper.Desktop/SettingsForm.cs:145`（"恢复默认"→显式预设）、`:135`（文案，Task 2 一并改）
- Test: `tests/OwHelper.Core.Tests/ArchitectureTests.cs`（新增源码扫描红线）

- [ ] **Step 1 写失败测试**（加进 `ArchitectureTests.cs`，复用其 `FindRepoRoot()`）

```csharp
[Fact]
public void MainForm_DoesNotApplyQuickPresets_OnStart()
{
    DirectoryInfo root = FindRepoRoot();
    string source = File.ReadAllText(Path.Combine(root.FullName, "src", "OwHelper.Desktop", "MainForm.cs"));
    Assert.DoesNotContain("QuickPresets.Apply", source);
}
```

- [ ] **Step 2 跑失败**：`dotnet test ow_helper.sln -c Debug --filter MainForm_DoesNotApplyQuickPresets` → FAIL（当前确实含该调用）
- [ ] **Step 3 实现**

```csharp
async System.Threading.Tasks.Task ToggleRunAsync()
{
    if (controller.Session.IsRunning) await controller.StopAsync();
    else await controller.StartAsync();          // 永远使用当前配置，不 ApplyPreset
    RefreshStatus();
}
```

`RefreshStatus()` 改为如实显示当前配置：

```csharp
string keys = string.Join("、", controller.Config.Input.Keys);
mainButton.Text = running ? "停止挂机" : "开始挂机";
mainSubtitle.Text = running
    ? $"正在自动按键（{keys} · 每 {controller.Config.Input.IntervalSeconds} 秒）"
    : $"当前：{keys} · 每 {controller.Config.Input.IntervalSeconds} 秒（点「更多设置」可改）"
      + (status.Pid == null ? "；先打开《守望先锋》" : "");
```

`BuildGuide()` 第 2 步文案改为「2. 点上面的「开始挂机」」。

- [ ] **Step 4 显式预设入口**：`SettingsForm` 的 `restore` 按钮改名为「恢复推荐设置」，行为改为把 `QuickPresets.TorbjornPass` 应用到**工作副本**（Task 2 引入克隆后生效；在此之前先写 `config.Input.Keys = new List<string>{"shift"}; config.Input.IntervalSeconds = 30; ...` 会污染 live 配置，故本 Step 与 Task 2 合并执行，或用 `new AppConfig()` 语义不变的现状先保留）。
- [ ] **Step 5 全量验证 + 提交**

```powershell
dotnet build ow_helper.sln -c Release
dotnet test ow_helper.sln -c Release
git add -A; git commit -m "fix(start): 主窗口启动不再隐式覆盖用户设置；按钮/副标题如实显示当前配置"
```

---

### Task 2（P0-02）：设置热应用语义统一

**Files**
- Modify: `src/OwHelper.App/Session.cs`（`ApplyConfig` 85-95、`ReapplyPolicyAsync` 97-113、`LoopAsync` 275-335）
- Modify: `src/OwHelper.Desktop/TrayController.cs:26-43`
- Modify: `src/OwHelper.Desktop/SettingsForm.cs:90-93,135,183-215`（工作副本 + 文案）
- Modify: `src/OwHelper.App/AppConfig.cs`（新增 `Clone()`）
- Test: `tests/OwHelper.App.Tests/SessionLifecycleTests.cs`（T03/T04）、`tests/OwHelper.App.Tests/AppConfigTests.cs`（Clone）

- [ ] **Step 1 失败测试**（`SessionLifecycleTests`，扩展 `FakePulseSender` 记录 `LastRecipe`）

```csharp
[Fact]
public async Task HotApply_NextPulseUsesNewKeys()
{
    var sender = new FakePulseSender();
    await using var session = BuildRunningSession(sender, intervalSec: 30);   // 复用本文件既有 fakes
    var updated = new AppConfig();
    updated.Input.Keys = new List<string> { "e" };
    updated.Input.IntervalSeconds = 30;

    await session.ApplyConfigAsync(updated);
    await Task.Delay(300);

    Assert.NotNull(sender.LastRecipe);
    Assert.Equal(KeyNames.Parse("e"), sender.LastRecipe!.Keys[0]);
}

[Fact]
public async Task HotApply_IntervalChange_RetimesCurrentWait()
{
    var sender = new FakePulseSender();
    await using var session = BuildRunningSession(sender, intervalSec: 60);
    await Task.Delay(200);                      // 第一次脉冲已发出，正在等 60s
    var updated = new AppConfig();
    updated.Input.Keys = new List<string> { "shift" };
    updated.Input.IntervalSeconds = 5;

    await session.ApplyConfigAsync(updated);

    long deadline = Environment.TickCount64 + 12000;
    while (sender.Calls < 2 && Environment.TickCount64 < deadline) await Task.Delay(100);
    Assert.True(sender.Calls >= 2, "间隔改为 5s 后当前等待应被重新计时");
}
```

（`BuildRunningSession` 用本文件现成的 `FakeLocator/FakeGovernor/FakePlacement`；`FakePulseSender.Execute` 里补 `LastRecipe = recipe;`）

- [ ] **Step 2 跑失败**：`dotnet test ... --filter HotApply` → FAIL（`ApplyConfigAsync` 不存在 / 不重计时）
- [ ] **Step 3 实现**

```csharp
// Session.cs
public async Task ApplyConfigAsync(AppConfig config)
{
    await gate.WaitAsync();
    try
    {
        ApplyConfig(config);
        if (!IsRunning) return;
        ReapplyPolicyLocked();
        wakeCts?.Cancel();                       // 让当前 Task.Delay 立即结束，下一轮按新 interval 重计时
    }
    finally { gate.Release(); }
}

ResourceApplyResult? ReapplyPolicyLocked()
{
    if (governor == null) return null;
    ResourceApplyResult applied = governor.Apply(Policy);
    LastResourceApplyPartial = !applied.Success;
    LogResourceApply(applied);
    if (LastResourceApplyPartial) Notify(new SessionNotice(SessionNoticeKind.ResourcePartialFailure));
    return applied;
}

public async Task<ResourceApplyResult?> ReapplyPolicyAsync()
{
    await gate.WaitAsync();
    try { return IsRunning ? ReapplyPolicyLocked() : null; }
    finally { gate.Release(); }
}
```

LoopAsync：每轮开头一次性取参 + CTS 生命周期：

```csharp
while (!ct.IsCancellationRequested)
{
    PulseRecipe recipeNow = recipe;
    int intervalNow = IntervalSec, jitterNow = JitterPercent;
    bool skipNow = SkipWhenForeground;
    ...
    pulseSender.Execute(current.Handle, recipeNow);
    ...
    wakeCts?.Dispose();
    using var wake = new CancellationTokenSource();
    wakeCts = wake;
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, wake.Token);
    try { await Task.Delay(NextWaitMs(intervalNow, jitterNow), linked.Token); }
    catch (TaskCanceledException) { if (ct.IsCancellationRequested) break; }
}
```

`NextWaitMs(int interval, int jitter)` 改为显式参数。

- [ ] **Step 4 持久化先行 + 工作副本**：`AppConfig.Clone()`（复用既有 `Options` 做 JSON round-trip，5 行）；`TrayController.ApplySettingsAsync` 顺序改为 `await session.ApplyConfigAsync(updated); updated.Save(...)` → **先 Save 成功后**再 apply；`SettingsForm` 只编辑 `controller.Config.Clone()`，取消永远不动 live 配置。文案统一为"保存后立即生效；正在挂机时下一次自动按键就用新设置。"
- [ ] **Step 5 全量验证 + 提交**

```
fix(config): 设置保存后原子热应用；间隔变更立即重新计时
```

---

### Task 3（P0-03）：WindowPlacement 事务化 pending 状态

**Files**
- Modify: `src/OwHelper.Core/WindowPlacement.cs`、`src/OwHelper.Core/Abstractions.cs:23-31`、`src/OwHelper.Core/Adapters.cs:15-46`
- Modify: `src/OwHelper.App/Session.cs`（`ToggleOffscreenAsync` 220-237、`CleanupAsync` 248-254、`FinishLocked` 456-462、`PersistPlacement` 259-271）
- Test: `tests/OwHelper.Core.Tests/WindowPlacementTests.cs`、`tests/OwHelper.App.Tests/SessionLifecycleTests.cs`（FakePlacement 接口同步）
- 新增测试缝：`WindowPlacement` 增加内部构造重载（Core.csproj 加 `InternalsVisibleTo Include="OwHelper.Core.Tests"`，与 Desktop 既有做法一致）

- [ ] **Step 1 失败测试**（用缝注入失败）

```csharp
[Fact]
public void Restore_StyleFailure_KeepsStylePending()
{
    using var window = new FakeWindow();
    using var self = Process.GetCurrentProcess();
    var placement = new WindowPlacement(window.Handle, self.Id,
        (_, _, _, _) => new WindowPlacementResult(true, "moved", null),
        (_, _, _) => new WindowStyleResult(false, "refused", 5, 0));
    placement.MoveOffscreenForTest(hideFromTaskbar: true);   // 内部缝同样驱动 hideFromTaskbar 路径

    var restore = placement.Restore(activate: false);

    Assert.False(restore.Success);
    Assert.True(placement.StyleRestorePending);
    Assert.False(placement.PositionRestorePending);
    Assert.True(placement.NeedsRestore);
    Assert.False(placement.IsOffscreen);        // 位置已恢复
}

[Fact]
public void Restore_PositionFailure_KeepsPositionPending_AndRetries()
{
    int attempts = 0;
    var placement = new WindowPlacement(hwnd, pid,
        (_, _, _, _) => ++attempts == 1
            ? new WindowPlacementResult(false, "SetWindowPos failed", 5)
            : new WindowPlacementResult(true, "moved", null),
        (_, _, _) => new WindowStyleResult(true, "ok", null, 0));
    ...MoveOffscreen...
    Assert.False(placement.Restore(false).Success);
    Assert.True(placement.IsOffscreen);
    Assert.True(placement.Restore(false).Success);
    Assert.False(placement.NeedsRestore);
}
```

- [ ] **Step 2 跑失败** → `WindowPlacement(..., moveTo, restoreStyle)` 不存在
- [ ] **Step 3 实现**（核心差异）

```csharp
public bool PositionRestorePending { get; private set; }
public bool StyleRestorePending { get; private set; }
public bool NeedsRestore => PositionRestorePending || StyleRestorePending;
public bool IsOffscreen => PositionRestorePending;      // 语义：位置仍待恢复

public WindowPlacementResult Restore(bool activate = false)
{
    if (!NeedsRestore) return new WindowPlacementResult(true, "nothing to restore", null);
    if (!Native.IsWindow(hwnd) || !PidMatches()) { State = PlacementState.Stale; return new(false, "window gone/pid mismatch; snapshot discarded", null); }
    if (WindowStyle.IsMinimized(hwnd)) WindowStyle.EnsureShown(hwnd, activate);

    string message = ""; int? error = null;
    if (PositionRestorePending)
    {
        var moved = moveTo(hwnd, pid, saved.Left, saved.Top);
        if (moved.Success) PositionRestorePending = false;
        else { message += moved.Message; error ??= moved.NativeError; }
    }
    if (StyleRestorePending)
    {
        var style = restoreStyle(hwnd, pid, originalExStyle);
        if (style.Success) StyleRestorePending = false;
        else { message += style.Message; error ??= style.NativeError; }
    }
    State = NeedsRestore ? PlacementState.RestoreFailed : PlacementState.None;
    return NeedsRestore
        ? new WindowPlacementResult(false, message, error)
        : new WindowPlacementResult(true, "restored", null);
}
```

MoveOffscreen 回滚：成功才清 `StyleRestorePending`，并记录回滚失败（保留 pending）。

- [ ] **Step 4 Session 接线**：`ToggleOffscreenAsync` 用 `placement.NeedsRestore` 判分支；`CleanupAsync`/`FinishLocked`/`TryReattachAsync` 改为 `if (placement.NeedsRestore) { ... if (!placement.NeedsRestore) ClearPlacementState(); }`；`PersistPlacement` 用 `StyleRestorePending`。`IWindowPlacementController` 用 `StyleRestorePending`+`NeedsRestore` 替换 `TaskbarHidden`。
- [ ] **Step 5 全量验证 + 提交**

```
fix(window): 事务化窗口恢复——只有外部状态真正恢复才清 pending
```

---

### Task 4（P0-04）：RuntimeRecovery 部分恢复不删文件

**Files**
- Modify: `src/OwHelper.App/RuntimeRecovery.cs`
- Test: `tests/OwHelper.App.Tests/RuntimeStateTests.cs`

- [ ] **Step 1 失败测试**（判定谓词 + 文件保留）

```csharp
[Fact]
public void FullyRestored_RequiresStyleWhenTaskbarHidden()
{
    var ok = new WindowPlacementResult(true, "ok", null);
    var fail = new WindowPlacementResult(false, "bad", 5);
    Assert.True(RuntimeRecovery.FullyRestored(ok, styleRequired: false, styleOk: true));
    Assert.False(RuntimeRecovery.FullyRestored(ok, styleRequired: true, styleOk: false));
    Assert.True(RuntimeRecovery.FullyRestored(ok, styleRequired: true, styleOk: true));
    Assert.False(RuntimeRecovery.FullyRestored(fail, styleRequired: false, styleOk: true));
}

[Fact]
public void TryRecover_PartialRestore_KeepsStateFile()
{
    using var window = new FakeWindow();
    using var self = Process.GetCurrentProcess();
    var store = new RuntimeStateStore(TempStatePath());
    WindowMover.MoveTo(window.Handle, self.Id, -10000, -10000);
    // 让样式恢复必然失败：把状态里的 pid 指成必然对不上的进程（验证会被 TargetValidation 拦截）
    // 因此这里改为：TaskbarHidden=true 且窗口随后被销毁 → RestoreStyle 返回 gone
    store.Save(new RuntimeState(self.Id, self.StartTime.ToUniversalTime(), window.Handle.ToInt64(), 111, 222, true, 0));
    ...
}
```

> 注：真实窗口无法制造"位置成功 + 样式拒绝"的组合（见 Part B 第 2 条）。因此该 Task 的可测部分为：
> ① `FullyRestored` 真值表单测；② 位置失败/窗口消失时**不**清理状态文件（可测）；③ 现有的成功路径回归。部分恢复的端到端路径列为本计划的残余风险（Part D）。

- [ ] **Step 2 跑失败** → 谓词不存在
- [ ] **Step 3 实现**

```csharp
WindowPlacementResult result = WindowMover.MoveTo(...);
bool styleOk = true;
if (state.TaskbarHidden)
{
    WindowStyleResult style = WindowStyle.RestoreStyle(...);
    styleOk = style.Success;
    if (!styleOk) output($"  任务栏图标恢复失败: {style.Message}（下次启动会重试）");
}
bool fullyRestored = FullyRestored(result, state.TaskbarHidden, styleOk);
if (fullyRestored) store.Clear();
else if (result.Success)             // 位置已恢复但样式未恢复：重存仍需恢复的部分
    store.Save(state with { Left = 当前位置, Top = 当前位置 });
output(fullyRestored ? "  上次的窗口状态已完全恢复" : "  只恢复了一部分，runtime-state.json 已保留");
return fullyRestored;
```

（`state with { ... }` 需要 `RuntimeState` 增加当前位置读取；若嫌麻烦，直接保留原 Left/Top 也可，代价见 Part D 备注。）

- [ ] **Step 4 全量验证 + 提交**

```
fix(recovery): 位置与样式全部恢复成功才清理 runtime-state.json
```

---

### Task 5（P0-05）：鼠标消息 wParam 状态机

**Files**
- Modify: `src/OwHelper.Core/MouseInput.cs`、`src/OwHelper.Core/PulseRunner.cs:30-82`、`src/OwHelper.Core/Native.cs`（补 `MK_SHIFT=0x0004`、`MK_CONTROL=0x0008`）
- Test: `tests/OwHelper.Core.Tests/MouseInputTests.cs:55-71`（改断言）、`tests/OwHelper.Core.Tests/PulseRunnerMouseTests.cs`（T09–T11）

- [ ] **Step 1 失败测试**

```csharp
[Fact]
public void SendButton_LeftButton_UpCarriesZeroState()
{
    ... 同现有用例，但断言 records[1].WParam == 0
}

[Fact]
public void Execute_LeftRightChord_ReflectsAggregateState()
{
    using var window = new FakeWindow();
    PulseRunner.Execute(window.Handle, new PulseRecipe { Keys = new[] { MouseInput.VkLeftButton, MouseInput.VkRightButton }, HoldMs = 20, SendFocus = false });
    var r = window.WaitFor(5);   // MOVE,LDOWN,RDOWN,RUP,LUP
    Assert.Equal(0x0001, (int)r[1].WParam);             // L down: L
    Assert.Equal(0x0003, (int)r[2].WParam);             // R down: L|R
    Assert.Equal(0x0001, (int)r[3].WParam);             // R up: L
    Assert.Equal(0x0000, (int)r[4].WParam);             // L up: 0
}

[Fact]
public void Execute_ShiftPlusLeft_CarriesShiftModifier()
{
    ... Keys = { 0x10 (Shift), VkLeftButton } ...
    // KEYDOWN 后鼠标 down: MK_SHIFT|MK_LBUTTON；up: MK_SHIFT
}
```

- [ ] **Step 2 跑失败** → chord 断言失败（当前恒为单键位）
- [ ] **Step 3 实现**（PulseRunner 内维护状态，按"消息发生瞬间"计算）

```csharp
int state = 0;                                    // MK_* 位集合
foreach (int vk in recipe.Keys)
{
    state = ApplyKeyDown(state, vk);              // 鼠标位 + Shift/Ctrl 位
    messages.Add(Key(hwnd, vk, down: true, mouseX, mouseY, state));
}
Thread.Sleep(recipe.HoldMs);
for (int i = recipe.Keys.Count - 1; i >= 0; i--)
{
    state = ApplyKeyUp(state, recipe.Keys[i]);
    messages.Add(Key(hwnd, recipe.Keys[i], down: false, mouseX, mouseY, state));
}
```

`MouseInput.SendButton(hwnd, vk, down, x, y, int state)`：LOWORD=state，XBUTTON HIWORD=索引；`SendMove(..., state)`。`ApplyKeyDown/Up`：0x01→MK_LBUTTON、0x02→MK_RBUTTON、0x04→MK_MBUTTON、0x05/0x06→0（仅靠 HIWORD）、0x10/0xA0/0xA1→MK_SHIFT、0x11/0xA2/0xA3→MK_CONTROL。

- [ ] **Step 4 全量验证 + 提交**

```
fix(input): 鼠标消息 wParam 按按下状态语义编码（含组合键与 Shift/Ctrl）
```

---

### Task 6（P0-06）：内部恢复不抢焦点

**Files**
- Modify: `src/OwHelper.Core/Native.cs`（补 `SW_SHOWNOACTIVATE = 4`）、`src/OwHelper.Core/WindowStyle.cs:12`、`WindowPlacement.cs`、`Abstractions.cs`、`Adapters.cs`
- Modify: `src/OwHelper.App/Session.cs`（`ToggleOffscreenAsync` 传 user 意图）
- Test: `tests/OwHelper.Core.Tests/WindowPlacementTests.cs`（T12）

- [ ] **Step 1 失败测试**

```csharp
[Fact]
public void Restore_NoActivate_DoesNotStealForeground()
{
    using var background = new FakeWindow(topLevel: true);
    using var target = new FakeWindow(topLevel: true);
    using var self = Process.GetCurrentProcess();
    var placement = new WindowPlacement(target.Handle, self.Id);
    placement.MoveOffscreen(hideFromTaskbar: false);

    bool foregroundSet = WindowProbe.TrySetForeground(background.Handle);   // TestSupport 补一个 SetForegroundWindow 包装
    if (!foregroundSet) return;                                            // 无交互桌面的 CI 上跳过强断言

    var restore = placement.Restore(activate: false);

    Assert.True(restore.Success);
    Assert.Equal(background.Handle, WindowProbe.GetForegroundWindow());
}
```

- [ ] **Step 2 跑失败** → 断言前台被 target 抢走（SW_RESTORE）
- [ ] **Step 3 实现**：`WindowStyle.EnsureShown(IntPtr hwnd, bool activate)` → `ShowWindow(hwnd, activate ? SW_RESTORE : SW_SHOWNOACTIVATE)`；`IWindowPlacementController.Restore(bool activate = false)`；仅 `Session.ToggleOffscreenAsync`（用户点「恢复游戏窗口」）传 `activate: true`，其余调用点保持默认 `false`。
- [ ] **Step 4 实机核对（人工）**：最小化的 OW 窗口用 NoActivate 恢复后是否正常显示（SW_SHOWNOACTIVATE 对最小化窗口的实机行为需确认；若无效，退回 `SetWindowPlacement(showCmd: SW_SHOWNOACTIVATE)` 方案，同测试复用）。
- [ ] **Step 5 全量验证 + 提交**

```
fix(window): 区分内部恢复（不激活）与用户主动恢复（激活）
```

---

### Task 7（P1-02/P1-12/§27.4）：目标选择健壮化 + 进程生命周期 + 失败即重连

**Files**
- Modify: `src/OwHelper.Core/GameWindow.cs:39-63`
- Modify: `src/OwHelper.App/Session.cs`（失败计数、reattach 后 Dispose 旧 target）
- Test: `tests/OwHelper.Core.Tests/TargetSelectionTests.cs`（新，用 `OwHelper.TestSupport.StandIn` 起两个同名进程：A 无有效窗口、B 有可见窗口）+ `SessionLifecycleTests`（连续失败触发 reattach）

- [ ] **Step 1 失败测试**：A 无窗口 / B 有 4000×3000 可见窗口 → `GameWindow.Find("StandIn")` 必须选中 B（当前会选中 A 并返回 null）
- [ ] **Step 2 实现**

```csharp
public static GameWindow? Find(string processName)
{
    var processes = Process.GetProcessesByName(processName);
    try
    {
        GameWindow? best = null; long bestScore = -1;
        foreach (Process p in processes)
        {
            foreach (GameWindow w in Enumerate((uint)p.Id, false, p))
            {
                long score = (w.Visible ? 1_000_000_000L : 0) + (long)w.Width * w.Height;
                if (score > bestScore) { bestScore = score; best = w; }
            }
        }
        return best;
    }
    finally
    {
        foreach (Process p in processes) if (p != best?.Process) p.Dispose();
    }
}
```

Session：`TryReattachAsync` 恢复旧资源后 `oldTarget?.Process.Dispose()`；`DisposeAsync` 里对当前 target 的 Process 同样 Dispose（只释放句柄，不杀进程）。连续脉冲失败 ≥2 次时调用 `current.Validate()`，无效则 `reattachRequested = true; wakeCts?.Cancel();`（不等下一轮 interval）。

- [ ] **Step 3 全量验证 + 提交**：`hardening(target): 多同名进程择优选窗 + 进程句柄释放 + 连续失败即时重连`

---

### Task 8（P1-03/P1-04-校验）：配置原子写 + schemaVersion + GPU 范围校验

**Files**
- Modify: `src/OwHelper.App/AppConfig.cs`（`Save`、`Load`、`Validate`）
- Test: `tests/OwHelper.App.Tests/AppConfigTests.cs`

- [ ] **Step 1 失败测试**：`interval=500` → 修正为 300；`gpuBackgroundFpsTarget=500` → 修正为 200 并给出 problem；写盘后存在 `config.json.bak`；损坏主文件时能从 bak 读回
- [ ] **Step 2 实现**

```csharp
public int SchemaVersion { get; set; } = 2;

public void Save(string path)
{
    string? dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    string tmp = path + ".tmp";
    File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
    if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
    File.Move(tmp, path, overwrite: true);
}
```

`Load`：主文件解析失败时尝试 `.bak`，成功则恢复（把 bak 复制回主文件）并加入 problems「已从备份恢复」；两者都失败 → 默认配置 + 保留损坏原文件。

- [ ] **Step 3 全量验证 + 提交**：`feat(config): 原子写 + 备份回退 + schemaVersion + GPU 帧率范围校验`

---

### Task 9（P1-09/§59）：日志事件名与 CTS 生命周期收尾

- [ ] `AppStartup.cs:26` `CONFIG_LOAD_FAILED` → `CONFIG_LOAD_WARNING`；`:34` 恢复消息 → `RECOVERY_FOUND` / `RECOVERY_SKIPPED` / `RECOVERY_SUCCESS` / `RECOVERY_PARTIAL` / `RECOVERY_STALE`（按 RuntimeRecovery 返回结果选择）
- [ ] `Session.LogResourceRestore` 部分失败 → `RESOURCE_RESTORE_PARTIAL`；`TrayController` 的 `CONFIG_APPLIED` → `CONFIG_SAVE`
- [ ] CTS：每轮 `wakeCts?.Dispose()`（Task 2 已含）
- [ ] 测试：`RuntimeStateTests`/`AppLogTests` 断言新事件名；提交：`chore(logs): 事件名按语义收敛（RECOVERY_*/CONFIG_LOAD_WARNING/CONFIG_SAVE）`

### Task 10（P1-06/P1-04-GUI/P1-08/P2-05-部分）：桌面端可见性与信息如实化

- [ ] 配置问题可见：`TrayApplicationContext` 读 `startup.Problems`，首次 Refresh 气泡「有 N 项设置已自动修正」+ 状态卡显示一行
- [ ] GPU 指引（仅检测到 NVIDIA 时）：状态卡加一行「GPU：建议在 NVIDIA 控制面板设置后台最大帧率 = {target} FPS」，不显示"已限制"
- [ ] 次数语义：`Session` 增加 `RunPulseCount`/`RunStartedAt`（Start 时归零），`TrayStatus` +2 字段，`PlainLanguage.PulseSummary` → 「本次自动按键 N 次 · 已运行 X 分钟」
- [ ] `SettingsForm`：`CancelButton = cancel`（Esc 关闭）
- [ ] 测试：`TrayStatusMapperTests`/`PlainLanguageTests` 更新；提交：`feat(desktop): 配置修正可见、GPU 指引、本次运行统计、Esc 关闭设置`

### Task 11（P1-07/P1-11）：单点化与 CI 发布冒烟

- [ ] `Desktop/Program.cs:14` → `new SingleInstance(AppStartup.SingleInstanceName)`；架构测试加「源码中 `OwHelper.SingleInstance` 字面量只出现一次」
- [ ] `ci.yml` 追加：

```yaml
      - name: Publish smoke
        shell: pwsh
        run: ./scripts/publish.ps1
      - name: Verify artifacts
        shell: pwsh
        run: |
          foreach ($f in 'OwHelper.Desktop.exe','OwHelper.exe','BgKeyProbe.exe') {
            if (-not (Test-Path "artifacts/OwHelper-win-x64/$f")) { throw "missing $f" }
          }
```

- [ ] 提交：`chore(ci): 互斥体常量单点化 + 发布路径冒烟`

### Task 12（P1-01，可选）：Power Throttling 原始状态快照

- [ ] `Native` 补 `GetProcessInformation` + `ProcessPowerThrottling` 查询；`ResourceGovernor` 首次 Apply 前保存 `(ControlMask, StateMask)`，Restore 优先回写原值，查询失败回退 system-managed
- [ ] 测试：`ResourceGovernorTests` 增"快照可读时恢复原值"（不可读时跳过强断言）
- [ ] 提交：`feat(resources): 恢复原始 Power Throttling 状态（读失败回退 system-managed）`

---

### 发布阶段（M3，Owner 决策后执行）

- [ ] Owner 选定 License → 删 `LICENSE-TODO.md`、加 `LICENSE`、README 更新
- [ ] 版本号（`0.2.0`）+ 关于/版本显示（P2-04）
- [ ] `tag v*` 工作流：双包（self-contained 推荐 + framework-dependent）+ `SHA256SUMS.txt` + GitHub Release
- [ ] README「下载」指向 Release
- [ ] 人工验收：4 小时 soak（前台 Chrome/IDE 无干扰、OW 重启自动重连、资源恢复）

---

## Part D. 验收与残余风险

**DoD（P0 完成时）**：193 既有测试全绿 + 新增失败路径测试全绿 + Release 0 警告 + CI 绿。

**残余风险 / 未覆盖（如实声明）**
1. 「位置恢复成功 + 样式恢复拒绝」的**端到端**路径无法用真实窗口制造（`SetWindowLongPtr` 成功后再校验必然一致），只覆盖了判定谓词与文件保留逻辑；计划用 Task 3 的测试缝覆盖组件层，运行时全链路依赖实机 soak 验证。
2. P0-06 的 `SW_SHOWNOACTIVATE` 对**最小化**窗口的实机行为需人工确认，必要时改 `SetWindowPlacement`。
3. Task 4 的部分恢复重存策略若保留旧 `Left/Top`，用户手动移动窗口后重试会"拉回原地"；可接受但需在提交信息中记录。
4. 4 小时 soak 与"游戏最小化"实测仍需用户侧执行（已列在 AGENTS.md 待办）。

---

## Part F. M1 执行记录（2026-09-18，一次性做完）

| Task | 提交 | 说明 |
|---|---|---|
| 1（P0-01） | `7a35e45` | MainForm 启动只调 `StartAsync`；按钮/副标题如实显示当前配置；`QuickPresets.ToConfig` + 设置页「恢复推荐设置」显式入口；`ArchitectureTests.MainForm_DoesNotApplyQuickPresets` 红线。本提交顺带入库了本计划文档 |
| 2（P0-02） | `ff56947` | `Session.ApplyConfigAsync`（gate+策略重应用+打断等待重计时）；等待抽成 `WaitForNextPulseAsync`（每轮重读间隔/抖动、CTS 释放）；`TrayController` 先落盘后应用；`SettingsForm` 工作副本 + 保存失败可见；删被取代的 `ReapplyPolicyAsync`；HotApply 单测（~5s） |
| 3（P0-03） | `c44b9a1` | `PositionRestorePending`/`StyleRestorePending`/`NeedsRestore`；成功才清 flag；回滚失败保留；`IWindowPlacementController` 同步；Core 加 `InternalsVisibleTo` 测试缝 |
| 4（P0-04） | `4f61f24` | `RuntimeRecovery.FullyRestored` 判定 + 真值表单测；部分恢复保留文件并提示重试 |
| 5（P0-05） | `c0f676f` | `MouseInput` 状态参数（6 参重载 + 单键便捷重载，保持 BgKeyProbe 不动）；`PulseRunner` 维护按下瞬间状态；chord/Shift 组合单测；修正了固化错误值的旧断言 |
| 6（P0-06） | `9543251` | `EnsureShown(hwnd, activate)`；`Restore(activate=false)` 默认；仅用户「恢复游戏窗口」传 true；接线单测（`Restore_ForwardsActivateFlag`、`S17`）全环境可跑；前台集成测试加能力自检（无交互桌面会话跳过） |

**执行中与计划的偏差**
- Task 6 原定的 `SetWindowPlacement` 回退方案已 revert：实证发现当前执行会话（无前台权限）连 `SW_RESTORE` 基线都无法恢复最小化窗口，属环境限制；改用文档化的直接 `ShowWindow(SW_SHOWNOACTIVATE)`，真实前台行为列入 soak 人工验收。
- Task 1 额外发现 `QuickPresets.Title/Subtitle` 在改完后只剩测试引用，已用设置页 ToolTip 接住（无死字段）。

**结果**：Release 0 警告；Core 119 + App 89 = **208/208**；两前端冒烟存活；6 提交已推送。

## Part G. M2/M3 执行记录（2026-09-18）

| Task | 提交 | 说明 |
|---|---|---|
| 7（P1-02/P1-12/§27.4） | `8137a2e` | `GameWindow.Find` 遍历全部同名进程按可见+面积择优、释放未选中句柄；连续失败+校验失效不等满间隔直接重连；会话拥有目标进程生命周期（重连/Dispose 释放）。执行中修了 S07（测试复用已释放句柄的 GameWindow，改为按生产路径刷新） |
| 8（P1-03/P1-04-校验） | `d642eba` | 配置 tmp+备份原子写、损坏回退备份、schemaVersion=2、GPU 帧率 20–200 钳制 |
| 9（P1-09） | `b6c625c` | `RecoveryOutcome` + 事件名收敛（RECOVERY_*/CONFIG_LOAD_WARNING/CONFIG_SAVE/RESOURCE_RESTORE_PARTIAL） |
| 10（P1-06/P1-04-GUI/P1-08） | `b9a124c` | 状态卡：配置修正警告（气泡+可点行）/NVIDIA 后台帧率指引（仅检出时）/本次按键+运行时长；Esc 关设置窗 |
| 11（P1-07/P1-11） | `bbc7d60` | 互斥体常量单点化+红线测试；CI 发布冒烟；顺手删了 artifacts 里残留的旧 `OwHelper.Tray.exe` |
| 12（P1-01） | `352cd05` | GetProcessInformation 快照原始 Power 状态并优先恢复，读失败回退 system-managed |
| M3（MIT/v1.0.0） | `08c3112` | LICENSE（MIT，SiyuanHao）+ 删 TODO；`Directory.Build.props` Version=1.0.0；标题栏显示版本；publish.ps1 双包开关；release.yml（tag→双包+SHA256+Release）；README 下载/License；AGENTS 同步 |
| 测试隔离修复 | `92da1fb` | **CI 曾红一次**：新多进程测试与 Live 集成测试并行抢同名替身进程池。用 xUnit Collection 串行化；本地连续两遍全绿后推送，CI 转绿 |

**版本号说明**：首个 Release 定为 **v1.0.0**（未采用 PRD 建议的 0.2.0，因仓库现役文档已自称 1.0.0）。

**结果**：Release 0 警告；Core 121 + App 103 = **224/224**；`tag v1.0.0` 已推，Release 创建成功（双包 + SHA256SUMS.txt），下载包内桌面端冒烟存活。

## Part E. 需要 Owner 决定

1. **P0 六项是否全做**（本计划按"全做"编排）；若只做 P0-01…P0-04（状态一致性四项），P0-05/P0-06 可降到 P1。
2. **Part B 的六条裁剪**是否有异议（尤其：不做 `IConfigMigration`、不做 docs 重组/ADR、P2 全推迟）。
3. **License**：发布阶段的前置，Coding Agent 不代选。
4. **PRD v2 文档是否入库**：目前仓库只有 `ow_helper_PRD_v1.0.md`，`ow_helper_PRD_v2.0_release_readiness.md` 未落盘；建议放仓库根目录与 v1 并列（需你确认）。
