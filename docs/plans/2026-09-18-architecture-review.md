# 架构审视与重构计划（2026-09-18）

> 依据：architecture-refactoring skill · 范围：仓库级（模块图 + 热点）· 产出：审计 + 计划（未执行）

## 1. 基线（Baseline）

| 项 | 值 |
|---|---|
| 构建 | `dotnet build ow_helper.sln -c Release` → 0 警告 0 错误 |
| 测试 | `dotnet test ow_helper.sln -c Release` → **193/193**（Core 110 + App 83，含实机替身集成测试） |
| 架构规则（已由测试强制） | ① app 程序集无 `DllImport` ② Core 不引用 App/前端 ③ `src/` 无注入类红线 API |
| 依赖方向（`dotnet list reference` 实证） | Core ← App ← {OwHelper(控制台), OwHelper.Desktop}；BgKeyProbe → Core；**无环** |

## 2. 证据地图（Evidence map）

| 模块 | 责任 | 状态归属 | 对外契约 |
|---|---|---|---|
| `OwHelper.Core` | Win32 互操作、目标发现/校验、脉冲、资源、窗口（含老板键样式）、GPU 检测、光标 | 无状态（每次调用即结果） | `GameWindow` `PulseRunner` `MouseInput` `ResourceGovernor` `WindowPlacement`/`WindowStyle` `KeyNames` `GpuEnvironment` + 4 个接口（测试缝隙） |
| `OwHelper.App` | Session 状态机（482 行，热点）、配置、日志、崩溃状态、单实例 | **运行期状态唯一所有者**（target/governor/placement/cts/notices） | `Session` `AppConfig` `AppLog` `RuntimeStateStore` `RuntimeRecovery` `SessionNotice` |
| `OwHelper`（控制台） | 技术前端：参数解析 + `ConsoleUi` 文案 | 无（转调 Session） | 进程退出码、控制台文案 |
| `OwHelper.Desktop` | 大众化前端：MainForm/设置/托盘/人话层/预设/色板 | 仅 UI 状态（mainForm、icon 颜色） | 窗口/托盘行为 |
| `BgKeyProbe` | 诊断实验工具 | 无 | 控制台命令 |

运行时数据流（自动化会话）：`config.json → AppConfig → Session（target/governor/placement）→ PulseRunner/ResourceGovernor/WindowPlacement → OW 窗口`；持久化：`config.json`（用户配置）与 `runtime-state.json`（崩溃恢复），日志：`AppLog`。

## 3. 诊断（Diagnosis）

### F1 — 前端启动粘合重复（**重构**）

**证据**：同一套启动序列写了两遍——
- 控制台 `src/OwHelper/Program.cs:16-32`（单实例→配置→日志+保留期→恢复）与 `:56-99`（Session 装配→退出钩子）
- 桌面 `src/OwHelper.Desktop/TrayApplicationContext.cs:28-61`（同样序列）与退出路径

**成本机制**：新增**一个适配器**（如新的输入通道）或**一个新的启动步骤**（如崩溃状态之外的持久化）要改 2 处前端，且两份必须保持顺序一致（初始化顺序是隐性契约：单实例 → 配置 → 日志 → 恢复 → Session）。

### F2 — 配置→Session 映射重复 3 处（**重构**）

**证据**：同一 8 项属性赋值 + `BuildRecipe()` 出现在
`src/OwHelper/Program.cs:65-90`、`TrayApplicationContext.cs:42-59`、`TrayController.ApplySettingsAsync:29-37`

**成本机制**：`AppConfig` 每新增一个会影响运行的字段（最近一天已发生 3 次：`jitterPercent`/老板键/前台跳过），必须同步改 3 处，漏一处即出现"设置了不生效"的静默缺陷。

### F3 — `Session.cs` 482 行、多职责（**不改，记录理由**）

它同时拥有：状态机、重连策略、脉冲循环、资源/窗口编排、通知队列、崩溃状态落盘。**判断：内聚**——这些都是"一个自动化会话"的同一生命周期，拆成 3 个类会让每次变更多 2 跳而语义耦合不减（skill：优先内聚而非碎化）。若未来出现**第二类会话**（多目标/多房间）再拆。

### F4 — 控制台保留技术文案（**不改**）

`ConsoleUi` 输出"第 N 次脉冲完成"等术语，与桌面的人话层有意分叉。定位差异（README 已声明控制台面向折腾党），统一会删掉技术前端的诊断价值。

### F5 — `AppMessages` 静态可变（**不改，记录取舍**）

桌面单实例应用、单会话，静态"最近消息"换取零管道；若未来支持多会话改为注入。

### F6 — 死代码：`PlainLanguage.StartHint`（**删除**）

**证据**：产品代码零调用（`grep StartHint`：仅 `PlainLanguage.cs` 定义 + `PlainLanguageTests` 两处引用）。被 `QuickPresets.Subtitle` 取代后遗留。

### F7 — 架构测试与依赖方向（**保持**）

3 条规则 + 无环依赖是当前最强约束，无需改动。

## 4. 排序（Ranking）

`value ≈ 变更频率 × 影响半径 × 缺陷成本 ÷ 迁移风险`

| 排名 | 项 | 变更频率 | 影响半径 | 缺陷成本 | 迁移风险 |
|---|---|---|---|---|---|
| 1 | **F1+F2 合并**（同一目标：装配单点化） | 高（设置项近期频繁新增） | 中（2 前端 +控制器） | 中（静默不生效/顺序漂移） | 低（纯搬运 + 测试） |
| 2 | F6 删死代码 | — | 低 | 低 | 极低 |
| 3 | F3/F4/F5 | — | — | — | 不改 |

## 5. 目标边界（Target boundary）

新增 `OwHelper.App/AppStartup.cs`（同一程序集，无新项目、无新依赖）：

- **责任**：把"启动一个会话"的固定顺序收成一处——单实例句柄 → 配置加载 → 日志（含保留期清理）→ 崩溃恢复 → Session 装配 → 配置应用。
- **契约**：`AppStartup.Initialize(string frontendTag, Action<string> output, Func<string,bool> confirmRecovery)` → `AppStartupResult { AppLog Log, AppConfig Config, RuntimeStateStore StateStore, Session Session, int? EarlyExitCode, IReadOnlyList<string> Problems }`；前端只负责：选择输出通道、选择确认 UI（控制台 ReadKey / 桌面 MessageBox）、注册退出钩子。
- **数据所有权**：不变（Session 仍唯一拥有运行期状态）。
- **隐藏知识**：初始化顺序、单实例名、恢复流程对前端不再可见。
- **依赖方向**：App 内部；前端 → App（不变）。

`Session.ApplyConfig(AppConfig)`：把 8 项属性 + Recipe 构造收进 Session（AppConfig 与 Session 同程序集，不引入依赖）；`ApplySettingsAsync` 保留"存盘 + 日志 + 运行中重应用策略"的桌面编排职责。

## 6. 迁移计划（每步可构建 + 测试全绿 + 独立提交）

```text
Step 1  Session.ApplyConfig + 单测（映射断言）           ← 新路径
Step 2  控制台 Program 迁移到 ApplyConfig               ← 迁移第一个调用方
Step 3  桌面 TrayController/Context 迁移到 ApplyConfig   ← 迁移其余调用方
Step 4  AppStartup 收拢两侧启动序列；删除 StartHint      ← 收口 + 删死代码
Step 5  文档同步 + 架构报告（before/after）              ← 收尾
```

**行为契约（必须不变）**：
- 控制台：全部文案、退出码（0/1/2）、参数解析语义；
- 桌面：界面文案/托盘行为/气泡、老板键语义、设置保存后的热应用与"按键下次生效"提示；
- 单实例互斥体名与拒绝行为；`config.json` 读写格式；`runtime-state.json` 字段含义；日志事件名与字段。

**每步验证**：`dotnet build -c Release`（0 警告）+ `dotnet test`（193，逐步增至 ~196）+ 涉及前端时手工冒烟（启动/退出/设置保存）。

**回滚**：每步单独提交；Step 4 若发现顺序语义差异立即回退（保留 Step 1-3 的收益）。

## 7. 风险与停止条件

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| `AppStartup` 固化顺序后前端需要不同顺序（如桌面要提前建托盘图标） | 低 | 中 | 契约暴露 `Action` 回调点，而非包办 UI |
| 迁移中把控制台文案顺手"人话化" | 中 | 低 | 行为契约明确禁止；只在 App 内部搬运 |
| 抽取后前端仍各自持有差异逻辑，收益不达预期 | 低 | 低 | 完成后用"新增一个设置项需要改几处"复核（目标：AppConfig + ApplyConfig 2 处） |

**停止/重估**：若 Step 2 后控制台行为出现任何文案差异，暂停并评估是否值得继续；若 `AppStartup` 的契约需要 3 个以上的可选参数才能覆盖两个前端，说明边界选错，退回"只做 Session.ApplyConfig"的最小方案。

---

# 执行报告（2026-09-18，已完成）

## Before / After

**Before**：启动序列（单实例→配置→日志→保留期→崩溃恢复→Session 装配→配置映射）在两个前端各写一遍（控制台 `Program.cs` 105 行、桌面 `TrayApplicationContext` 226 行）；`config → Session` 的 8 项映射复制 3 份（控制台 / 桌面构造 / `TrayController.ApplySettingsAsync`）；`PlainLanguage.StartHint` 为死代码。

**After**：
- `OwHelper.App/AppStartup.cs`（43 行）：启动顺序唯一归属；前端只提供「输出通道、恢复确认 UI、退出钩子」；
- `Session.ApplyConfig(AppConfig)`：配置→会话映射唯一归属（495 行 Session 内 ~10 行）；
- 控制台 `Program.cs` 105 → **77 行**；桌面 `TrayApplicationContext` 226 → **170 行**；`SettingsForm` 仍只写配置（未受映射重构影响）；
- 删除 `PlainLanguage.StartHint` 及其测试。

**Coupling removed**：启动顺序知识的 2 份复制；配置映射的 3 份复制（实测：产品代码中 `KeepOffscreenAcrossRestart =` 只剩 `Session.ApplyConfig` 1 处，另一处是设置界面写配置，属不同职责）。

**Coupling introduced**：前端 → `AppStartup`（App 层内的一个静态入口）。新耦合优于旧耦合：旧的是"两份必须人肉保持顺序一致"的隐性契约，新的是单点显式契约。

**Why the new coupling is preferable**：新增一个设置项现在只需改 `AppConfig` + `Session.ApplyConfig`（2 处）；新增一个启动步骤只需改 `AppStartup`（1 处）。实测映射点 3 → 1。

## Behavior verification

- `dotnet build ow_helper.sln -c Release`：0 警告 0 错误；
- `dotnet test`：**193/193**（Core 110 + App 83；`S14_ApplyConfig_MapsEveryField` 为新增映射测试，删除 1 个死代码测试）；
- 冒烟：控制台启动存活、桌面启动存活（托盘窗口正常）；
- 行为契约保持：控制台文案/退出码/参数解析、桌面界面与老板键语义、互斥体名、配置与运行状态文件格式、日志事件名。
- **已知微差（有意且已记录）**：控制台在"CLI 参数非法"时不再先打印配置问题行（因为参数解析现在先于 `AppStartup`）；非法参数退出码仍为 1、文案不变。

## Architecture verification

- 依赖方向（`dotnet list reference` 实测）：Core ← App ← {控制台, 桌面}，无环、无反向依赖；
- 架构测试（无 P/Invoke 泄漏 / Core 不引用 App / 源码无红线 API）：随 193 项测试通过；
- 变更传播复核：映射点 3 → 1（证据见上）。

## Remaining risks

- `Session.cs`（493 行）仍是热点且多职责（F3 已评估为内聚，暂不拆）；若未来出现"第二类会话"再评估拆分；
- `AppMessages` 静态可变（F5）在多会话场景需改为注入；
- 桌面 UI 在 150% DPI 下的观感需用户实机确认（布局已改为流式 + DIP 尺寸）。
