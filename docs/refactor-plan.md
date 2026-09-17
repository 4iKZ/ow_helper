# 架构重构计划：OwHelper 从玩具到标准应用

> 依据：architecture-refactoring skill 工作流（证据 → 诊断 → 目标边界 → 备选方案 → 排序 → 迁移计划）。
> 状态：计划阶段，尚未执行。

## 目标（Goal）

**要解决的架构问题：** 两个程序（OwHelper、BgKeyProbe）是同一套 Win32 能力的两份复制粘贴，且"脉冲配方"这一最常变的资产没有单一归属地——今天一天内它已经变了 4 次。

**具体证据：**

1. **重复的互操作层**：6 个 `DllImport` 声明 + `RECT` 结构体在两个文件中各写了一份（`PostMessage`、`MapVirtualKey`、`EnumWindows`、`GetWindowThreadProcessId`、`GetClassName`、`GetWindowRect`），合计约 135 行重复。
2. **逻辑重复且已漂移**：
   - 窗口枚举：probe 的 `WindowsOf(pid)`（含子窗口、带 depth）vs app 的 `FindOw()`（只取最大窗口）——同一模式两种实现。
   - lParam 构造：`KeyLParam(scan, up, repeat)` vs `KeyLParam(vk, up)`——签名不同、职责不同。
   - 脉冲执行：probe 的 `MatrixPulse`（带光标保护、逐条报错）vs app 的 `Pulse`（不带光标保护、**不检查任何返回值**）。
3. **已产生实际缺陷风险**：app 的 `Pulse()` 忽略 `PostMessage` 返回值。若某天消息发送失败（权限、窗口重建），工具会永远打印"第 N 次脉冲完成"而实际什么也没做——静默失败。probe 有报错，app 没有。
4. **零测试、零解决方案文件**：`dotnet build` 在仓库根目录不可用（必须逐项目构建）；今天验证的配方知识（`WM_SETFOCUS → 按键 → WM_KILLFOCUS`）只存在于运行时的实验结论里，任何"清理式重构"都可能无声破坏它。
5. **单文件全静态**：每个 app 一个 `Program` 类，互操作、窗口发现、输入注入、进程节流、调度循环、控制台 UI 全部混在一起（316 / 365 行）；静态可变状态跨线程共享（`volatile` + `Thread.Join` + 自连接守卫）；策略（"前台跳过"）藏在注入函数里。

**期望的变更传播改善：**
- 改"脉冲配方"（按键、时序、消息组合）→ 只动 **1 个文件**（`PulseRecipe` 定义处），不再两个程序各改一遍。
- 加新前端（托盘 UI）→ 不动脉冲逻辑。
- 保护配方：跑 `dotnet test` 就能验证消息序列，不需要开游戏。

## 非目标（Non-goals）

- 不新增功能：托盘 UI、配置持久化、NVIDIA 自动化、鼠标输入、多账号——全部不在本次范围。
- 不改已验证的配方行为（见行为契约）。
- 不设 CI（可选后续）；不做重命名/美化；不重构实验结论本身。

## 行为契约（Behavior contract）

**必须保持不变：**

- 脉冲消息序列：`WM_SETFOCUS → 按键按下（按顺序）→ 保持 200ms → 抬起（逆序）→ WM_KILLFOCUS`，focus 等待 50ms。
- OW 处于真实前台时跳过本次脉冲。
- 间隔 = 配置秒数 × [0.85, 1.15)，下限 1000ms；`+`/`-` 以 5s 步进，范围 5–300s。
- 开始挂机：`PriorityClass = BelowNormal` + EcoQoS 执行节流开启；停止/退出：恢复 `Normal` + 关闭节流。
- `m`：窗口移到 (-10000, -10000)（尺寸不变），再按还原到原坐标。
- 窗口选择：Overwatch.exe 进程的最大面积顶层窗口。
- 控制台命令与参数格式不变（空格/m/r/+/-/q；参数 `[按键,逗号分隔] [间隔秒]`）。

**有意的行为变更（仅一处，可选步骤）：**

- 脉冲失败上报：连续 N 次消息发送失败时打印警告并建议管理员运行（修复静默失败）。默认 N=3，只警告不自动停止。

## 当前状态（Current state）

- **所有权**：每个 app 一个静态类，各自拥有一切；配方知识没有模块归属（内联在 `Pulse`/`MatrixPulse` 里）。
- **依赖方向**：两个 app 是叶子，无共享库；它们之间的"耦合"是复制粘贴产生的隐性耦合。
- **公开接口**：无（没有库）。
- **当前风险**：双份维护漂移（已发生）；静默失败；配方知识无测试保护；线程生命周期靠 `volatile` + 自连接守卫，脆弱。

## 目标状态（Target state）

```
ow_helper/
├── ow_helper.sln
├── src/
│   ├── OwHelper.Core/          # 类库：唯一真源
│   │   ├── Native.cs           #   仅互操作：P/Invoke、常量、结构体（公开、无逻辑）
│   │   ├── GameWindow.cs       #   发现/枚举 Overwatch 窗口；IsAlive/IsForeground/Rect
│   │   ├── PulseRecipe.cs      #   数据：keys[]、激活消息开关、时序
│   │   ├── PulseRunner.cs      #   执行配方，返回逐条消息结果（成功/错误码）
│   │   ├── ResourceGovernor.cs #   优先级 + EcoQoS 施加/还原
│   │   └── WindowPlacement.cs  #   移出屏幕/还原（保存原坐标）
│   ├── OwHelper/               # 产品（控制台）：参数解析 + ReadKey UI + Session（循环+组合）
│   └── BgKeyProbe/             # 诊断（控制台）：窗口清单 + 矩阵命令 + 实验循环
├── tests/
│   └── OwHelper.Core.Tests/    # 假窗口靶 + 单元/集成测试
└── docs/
    └── refactor-plan.md
```

- **责任边界**：Core 只认识 Win32 和 BCL，不认识控制台；UI/日志由 app 负责；"前台跳过"是 app 策略，不在 Core 里。
- **数据所有权**：`PulseRecipe` 是配方的唯一真源；`GameWindow` 持有 hwnd/pid；`ResourceGovernor` 持有进程句柄；`WindowPlacement` 持有原坐标。跨线程状态收敛到一个 `Session`（app 内），循环用 `CancellationToken`，不再有静态可变字段。
- **公开契约**：`GameWindow.Find/EnumerateAll`、`PulseRunner.Execute(hwnd, recipe) → PulseResult`、`ResourceGovernor.Apply/Restore`、`WindowPlacement.MoveOffscreen/Restore`。
- **变为私有的知识**：消息码、lParam 位布局、扫描码映射、枚举选择规则、EcoQoS 结构体——全部封进 Core，app 不再知道。
- **依赖方向**：apps → Core；Core → BCL。反向依赖不存在。

## 备选方案对比（Alternatives）

| 方案 | 内容 | 评价 |
|---|---|---|
| A. 什么都不做 | 保持玩具，遇到问题再补 | 被否：静默失败缺陷真实存在；配方改动成本随实验次数线性增长 |
| B. 合并为一个程序 | probe 作为隐藏命令并入产品 | 被否：诊断 UI（窗口清单、矩阵、实验循环）会污染产品交互；两者演化节奏不同 |
| **C. 共享 Core 类库 + 两个薄前端（推荐）** | 见目标状态 | 复制粘贴消除；配方单点；可测试；前端可替换（未来托盘） |
| D. 只加解决方案文件 + 测试，不抽库 | 低风险，但不解决重复与漂移 | 部分有效；可作为 C 的降级预案 |

## 优先级排序（Ranking）

`value ≈ 变更频率 × 影响半径 × 缺陷成本 ÷ 迁移风险`

1. **抽 Core（含统一脉冲实现 + 失败上报）**——高频变更资产单点化 + 修复静默失败，迁移风险低。
2. **测试项目（假窗口靶）**——保护今天来之不易的配方知识，防止未来重构无声破坏。
3. **生命周期清理（CTS/Task 替代 volatile+Join）**——降低长期挂机的不确定性。
4. **UI 与策略分离（Session）**——为未来托盘 UI 铺路，属结构调整顺带完成。
5. 仓库布局 + 解决方案文件——低价值但极低成本，可先做（步骤 0）。

## 迁移策略（Migration strategy）

每步独立提交、可构建、可停留在任意安全点。行为契约之外的改动一律不做。

### 步骤 0 — 布局与解决方案（纯移动，不改代码）

- `git mv` 两个项目到 `src/`，新建 `ow_helper.sln` 并在根目录验证 `dotnet build` / `dotnet run`。
- 验证：两个可执行文件仍能编译，产物路径变化记录到 README 之外的口头交接（不新增文档）。
- 回滚：单个 `git revert`。

### 步骤 1 — 引入接缝（Core：Native + GameWindow）

- 新建 `OwHelper.Core`，把互操作声明**逐字**迁入 `Native.cs`；把窗口枚举逻辑迁入 `GameWindow`（含 probe 的 `WindowsOf` 与 app 的"最大窗口"选择）。
- 两个 app 改为引用 Core，删除各自重复的声明与枚举代码；其余逻辑不动。
- 架构效果：重复互操作层消失；窗口知识单点。
- 行为验证：`dotnet build` 两个 app；**实机冒烟**——probe 的 `b` 仍触发炮台、app 脉冲仍生效。
- 安全停止点：app 其余逻辑尚未迁移，完全可停。

### 步骤 2 — 新路径 + 测试（Core：Recipe/Runner/Governor/Placement）

- 实现 `PulseRecipe`（数据）、`PulseRunner`（逐条返回结果）、`ResourceGovernor`、`WindowPlacement`；app 尚未接入。
- 新建测试项目：
  - 假窗口靶：测试进程内创建一个隐藏窗口（WndProc 记录收到的消息），对靶执行配方，断言序列与参数（focus → down(s) → up(s) → killfocus、lParam 位、按键顺序）。
  - 纯单元：`VkOf` 解析、lParam 位布局、抖动区间 [0.85,1.15]、`ResourceGovernor.Apply/Restore` 对自身进程的往返。
- 依赖风险：xUnit 需 NuGet 还原；若离线失败，降级为零依赖控制台测试运行器（`dotnet run` 即测）。
- 行为验证：`dotnet test` 全绿；实机不受影响（未接线）。
- 安全停止点：新路径独立存在，随时可弃。

### 步骤 3 — 迁移第一个调用方（BgKeyProbe）

- probe 的矩阵命令改为"配方数据 + Core 执行"；`p/s/h` 实验循环复用 Core 原语（光标保护留在 probe 侧）。
- 架构效果：probe 不再自持互操作与配方；矩阵新增实验 = 加一条配方数据。
- 行为验证：实机跑 `b`、`u`、`p` 各一次确认效果不变。
- 安全停止点：产品仍走旧路径，可停。

### 步骤 4 — 迁移产品（OwHelper）

- `Pulse` → `PulseRunner(focus 配方)`；节流 → `ResourceGovernor`；`m` → `WindowPlacement`；`volatile+Thread` → `Session(CTS, Task)`；加入"连续失败警告"（契约中唯一有意变更）。
- 行为验证：实机冒烟全套——空格起停、30s 节奏、前台跳过、`m` 往返、节流状态打印与退出还原、Ctrl+C 清理。
- 安全停止点：旧代码删除前仍可对照。

### 步骤 5 — 删除旧路径并固化边界

- 删除 app 中残留的本地互操作/配方代码；测试项目追加一条架构断言：products/probe 程序集中不允许存在 `DllImport`。
- 架构验证：`dotnet test` 含架构断言通过；两 app 均不再 import `System.Runtime.InteropServices`（除 Core）。
- 行为验证：一步骤 4 的冒烟套件重跑一遍。

## 兼容性策略（Compatibility strategy）

- 无外部 API/持久化，兼容面 = 命令行接口与控制台交互：签名与按键不变（见行为契约）。
- 无临时适配层；旧代码路径在步骤 5 直接删除（迁移期间共存，不在运行时共存）。

## 验证矩阵（Verification matrix）

| 检查 | 基线（当前） | 每步后 | 最终 |
|---|---:|---:|---:|
| `dotnet build`（解决方案级） | ⚠ 无 sln，逐项目通过 | ✓ | ✓ |
| 单元测试 | ✗ 无 | ✓（步骤 2 起） | ✓ |
| 假窗口序列集成测试 | ✗ 无 | ✓（步骤 2 起） | ✓ |
| 架构断言（无 DllImport 泄漏） | ✗ 无 | — | ✓（步骤 5） |
| 实机冒烟（probe b/p；app 起停节奏） | 今天已验证 | 步骤 3、4 各一次 | ✓ |

## 风险（Risks）

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| 迁移中改动时序常量 → 配方失效 | 低 | 高（挂机失效） | 常量逐字迁移；假窗口测试钉死序列；每步实机冒烟 |
| 假窗口靶与真实 OW 行为不同 | 中 | 低（测试只验序列，不验游戏接受） | 实机冒烟作为真值来源 |
| xUnit 离线还原失败 | 中 | 低 | 降级零依赖测试运行器 |
| CTS 重构引入线程缺陷 | 低 | 中 | Session 单线程循环 + 取消令牌；小步提交 |
| 重构期间用户无法实机验证（游戏不在场） | 中 | 低 | 保留 probe 作对照；实机验证可推迟到步骤 4 后一次性做 |

## 停止/重估条件（Stop / reassess）

- Core 的公开面开始大于它隐藏的知识（抽象税超过收益）→ 回退到方案 D；
- 假窗口测试无法稳定复现序列 → 放弃集成测试，仅保留纯单元测试与实机冒烟；
- 迁移需要改变行为契约才能完成 → 暂停，重新划界。
