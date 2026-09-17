# OW Helper 产品需求文档（PRD）

> 文档版本：v1.0  
> 基线日期：2026-09-17  
> 适用仓库：`4iKZ/ow_helper`  
> 代码基线：`8938d85 chore: apply ponytail-audit cuts...`  
> 目标读者：Coding Agent / 开发工程师 / 代码审查者  
> 目标平台：Windows 11 x64，.NET 8，Overwatch 2（当前产品目标）  
> 文档性质：产品需求 + 技术约束 + 实施计划 + 验收规范

---

## 0. Executive Summary

OW Helper 当前已经从一个验证性质的 PoC 演化为一个能够稳定完成“后台定向按键 + 基础 CPU 节流 + 窗口移出屏幕”的 Windows 小工具。当前最关键的技术假设已经被实际验证：即使 Overwatch 窗口失去焦点甚至处于后台，工具通过向目标 HWND 投递 Windows 消息，仍可以让游戏接收到特定按键序列，同时用户可以继续操作浏览器、IDE、聊天软件等前台应用。

因此，下一阶段不应继续围绕“后台按键到底能不能实现”做探索，而应进入工程化阶段：把当前可工作的核心能力做成一个**可长期运行、可恢复、可诊断、不会误伤其他窗口、资源控制结果可验证、退出后不残留系统状态**的正式工具。

本 PRD 的核心目标不是大规模增加新功能，而是完成四件事：

1. **把 Session 生命周期做正确**：解决重新 Attach、OW 重启、窗口句柄变化、窗口位置恢复、资源状态恢复等边界问题。
2. **把资源控制做可信**：从“调用了 API 就打印成功”升级为“每个动作都有结果、失败可见、退出能够恢复原始状态”。
3. **把 GPU 后台占用纳入产品方案**：优先使用 NVIDIA 官方驱动的 Background Application Max Frame Rate；对自动化修改驱动配置采取保守策略。
4. **建立长期无人值守运行能力**：让工具在数小时级运行中遇到目标窗口重建、游戏重启、权限问题、API 调用失败时能够合理处理，而不是静默失效。

本版本明确禁止新增任何反作弊绕过、隐藏进程、DLL 注入、远程线程、读写游戏内存、内核驱动、Hook 游戏函数等能力。工具只能使用常规 Windows API、公开的系统调度接口，以及可验证的厂商公开接口。原因不仅是工程风险，也包括账号与平台合规风险。

---

# 1. 背景与问题定义

## 1.1 用户真实需求

用户希望在 Windows 11 上运行 Overwatch 2 时实现如下工作流：

1. 游戏已经进入一个需要周期性按下某个按键的场景。
2. 用户不希望继续用重物压住实体键盘。
3. 用户不希望游戏必须始终处于前台。
4. 用户希望将游戏放到后台后，继续正常使用整台电脑处理其他事务。
5. 游戏在后台时应尽可能少占用 CPU/GPU 等资源。
6. 用户希望工具能够长期运行，遇到窗口重建、游戏重启或暂时失效时，不需要频繁人工干预。
7. 停止工具后，应恢复游戏原有的系统状态，不得留下异常优先级、异常窗口位置或其他残留。

因此，本产品本质上不是一个普通“键盘宏”，而是一个面向指定 Windows 进程/窗口的：

> **后台输入调度器 + 进程资源控制器 + 窗口生命周期管理器 + 运行状态诊断工具。**

## 1.2 当前方案已经证明的能力

当前代码基线已经证明以下能力可行：

- 根据进程名 `Overwatch` 查找目标进程；
- 枚举目标进程的顶层窗口并选择最大面积窗口；
- 通过 `PostMessage` 向指定 HWND 投递：
  - `WM_SETFOCUS`
  - `WM_KEYDOWN`
  - `WM_KEYUP`
  - `WM_KILLFOCUS`
- 前台真实焦点仍可留给其他应用；
- 支持用户配置单个或多个虚拟键；
- 支持周期触发；
- OW 在真实前台时可以跳过脉冲；
- 通过 `Process.PriorityClass = BelowNormal` 降低 CPU 调度优先级；
- 通过 `SetProcessInformation(ProcessPowerThrottling, ...)` 请求 EcoQoS；
- 通过 `SetWindowPos` 把窗口移到屏幕外；
- 使用 `CancellationTokenSource + Task` 管理循环；
- 通过 FakeWindow 对消息顺序进行测试。

这些能力构成产品 v1 的可复用基础，不应推翻重写。

---

# 2. 外部调研结论与技术事实

本节用于约束 Coding Agent，避免实现过程中基于错误假设做设计。

## 2.1 Windows 后台消息输入

当前实现通过 `PostMessage(hwnd, WM_KEYDOWN/WM_KEYUP, ...)` 向指定 HWND 发送消息。该方式和 `SendInput` 有本质区别：`SendInput` 进入系统全局输入流，通常由当前前台应用消费；`PostMessage` 是向指定窗口的消息队列投递消息，因此不会天然抢占全局键盘焦点。

但必须保留一个事实：

- `PostMessage` 返回成功，只表示消息成功进入目标线程消息队列；
- 不等于目标游戏一定会处理该消息；
- 真实 Overwatch 是否接受该消息仍以实机行为为最终真值。

当前产品已经实机验证该路线对现有目标行为有效，所以 v1 应继续使用，不得为了“理论更底层”而换成侵入性更高的实现。

Microsoft 对 `WM_KEYDOWN` 的 `lParam` 位定义明确要求：

- bit 0-15：repeat count；
- bit 16-23：scan code；
- bit 24：extended-key flag；
- bit 30：previous key state；
- bit 31：transition state。

当前实现对于普通 Shift/W/A/数字/F1-F12 足够，但对于方向键、右 Ctrl、右 Alt、Insert/Delete/Home/End/PageUp/PageDown、数字键盘部分扩展键，需要正确设置 extended-key 位。

参考：
- https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-keydown
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagea

## 2.2 Windows EcoQoS / Power Throttling

Microsoft 明确支持通过 `SetProcessInformation(ProcessPowerThrottling, PROCESS_POWER_THROTTLING_STATE)` 控制进程 QoS。

当：

```text
ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
StateMask   = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
```

进程会被标记为 EcoQoS，系统会尝试通过更低频率、更高能效核心等方式降低功耗和热量。

关键点：

**恢复默认系统托管行为，不应简单地把 StateMask 置 0 而保持 ControlMask=EXECUTION_SPEED。**

Microsoft 文档给出的“恢复系统默认管理”方式是：

```text
ControlMask = 0
StateMask   = 0
```

当前代码的 `Restore()` 实际是：

```text
ControlMask = 1
StateMask   = 0
```

这更接近“明确关闭 execution speed throttling / 请求高 QoS”，而不是“把控制权还给系统”。因此新版本必须修正该语义。

参考：
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_power_throttling_state

## 2.3 CPU Priority

`BELOW_NORMAL_PRIORITY_CLASS` 是 Windows 官方支持的进程优先级，位于 Normal 与 Idle 之间，适合作为非前台关键任务的保守降级。

当前问题不在“BelowNormal 能不能用”，而在于：

- 现版本 `Restore()` 强制设置回 `Normal`；
- 如果用户启动工具前 Overwatch 本身已经是 `AboveNormal`、`Idle` 或其他状态，工具会破坏用户原设置。

正确需求：Attach 时记录原始 PriorityClass，Stop/Exit/Detach 时恢复原始值。

参考：
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass

## 2.4 GPU 后台限帧

NVIDIA 官方驱动支持：

> Background Application Max Frame Rate

其作用是限制后台运行的游戏/3D 应用最大渲染帧率，官方说明其目的就是减少功耗与风扇噪声，范围为 20-200 FPS。

参考：
- https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Manage_3D_Settings_%28reference%29.htm

NVIDIA 2026 年 NVAPI 仍然公开 DRS（Driver Settings）API，例如：

- `NvAPI_DRS_CreateSession`
- `NvAPI_DRS_LoadSettings`
- `NvAPI_DRS_FindApplicationByName`
- `NvAPI_DRS_GetSetting`
- `NvAPI_DRS_SetSetting`
- `NvAPI_DRS_SaveSettings`

参考：
- https://docs.nvidia.com/nvapi/group__drsapi.html
- https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h

但产品 v1 不应直接依赖未充分验证的驱动 setting ID 或第三方反向工程值。GPU 自动配置必须分阶段处理：

- v1：检测 NVIDIA 环境 + 给出明确指导 + 提供状态提示；
- v1.x：只有在确认使用 NVIDIA 官方 NVAPI 中公开、稳定的 setting ID 与值语义后，才允许自动修改特定应用 profile；
- 任何自动修改必须保存旧值，并可恢复。

## 2.5 Job Object CPU Hard Cap

Windows Job Object 支持 CPU rate control，例如 hard cap 或 min/max rate。理论上可以给进程设 20%、30% 等 CPU 上限。

参考：
- https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information

但本产品不应在 v1 默认使用 Job Object 硬限 CPU，原因：

1. 游戏进程可能已经属于其他 Job；
2. 硬 CPU cap 容易导致逻辑帧、网络心跳、资源流加载抖动；
3. 本产品的目标是降低后台资源占用，而不是追求最低绝对 CPU；
4. BelowNormal + EcoQoS + GPU 后台限帧已经是更低风险的第一选择。

Job Object CPU cap 只能作为后续实验功能，默认关闭。

## 2.6 平台规则风险

Blizzard 当前 EULA 对未经明确授权、能够自动控制游戏或角色的软件定义非常宽泛，并明确列出 bots / automated control。

参考：
- https://www.blizzard.com/en-us/legal/08b946df-660a-40e4-a072-1fbde65173b1/blizzard-end-user-license-agreement
- https://www.blizzard.com/en-us/legal/cd5930c0-2784-420c-a23d-1e0d6ff8599b/anti-cheating-agreement

因此本 PRD 对 Coding Agent 做以下硬约束：

- 不实现任何反作弊规避；
- 不隐藏进程；
- 不伪造签名；
- 不对 Battle.net/Overwatch 进程做 DLL 注入；
- 不调用 `WriteProcessMemory` / `ReadProcessMemory` / `CreateRemoteThread`；
- 不 Hook 游戏函数；
- 不开发内核驱动；
- 不以“降低被检测概率”为目的设计随机行为；
- 不加入任何“anti-ban / undetected / bypass”能力或文案。

产品只做普通 Windows 级窗口消息与资源调度。

---

# 3. 当前仓库审计结果

## 3.1 当前项目结构

```text
ow_helper/
├── ow_helper.sln
├── src/
│   ├── OwHelper.Core/
│   │   ├── Native.cs
│   │   ├── GameWindow.cs
│   │   ├── PulseRecipe.cs
│   │   ├── PulseRunner.cs
│   │   ├── ResourceGovernor.cs
│   │   ├── WindowPlacement.cs
│   │   ├── CursorState.cs
│   │   └── KeyNames.cs
│   ├── OwHelper/
│   │   ├── Program.cs
│   │   └── Session.cs
│   └── BgKeyProbe/
│       └── Program.cs
├── tests/
│   └── OwHelper.Core.Tests/
└── docs/
    └── refactor-plan.md
```

当前架构总体方向正确：

- Apps → Core 单向依赖；
- Win32 P/Invoke 集中在 Core；
- 输入配方与窗口发现已收敛；
- 产品前端与诊断 Probe 分离。

此结构应继续演进，不建议重新合并成单文件应用。

## 3.2 当前主要缺陷

### BUG-001：重新 Attach 会丢失窗口恢复状态

`Session.Attach()` 当前会：

```csharp
target = GameWindow.Find(ProcessName);
governor = new ResourceGovernor(target.Process);
placement = null;
```

如果用户已经执行 `m` 将窗口移出屏幕，此时再执行 `r`：

- 原 `WindowPlacement` 被丢弃；
- 原坐标状态丢失；
- Cleanup 时无法还原；
- 游戏窗口可能永久留在 `(-10000, -10000)`。

这是 P0 缺陷。

### BUG-002：OW 重启后资源策略不会重新 Apply

运行循环发现 target 死亡时会 `Attach()` 新目标，但不会重新执行：

```csharp
governor.Apply();
```

结果：

- 输入恢复；
- 但新 Overwatch 进程没有 BelowNormal / EcoQoS；
- 产品看起来仍在工作，但资源控制已静默失效。

这是 P0 缺陷。

### BUG-003：Restore 强制 Priority=Normal

当前 `ResourceGovernor.Restore()`：

```csharp
process.PriorityClass = ProcessPriorityClass.Normal;
```

应恢复 Attach 时原始 PriorityClass，而不是假定原始值一定是 Normal。

这是 P1 缺陷。

### BUG-004：EcoQoS 恢复语义不准确

当前 `SetEcoQoS(false)` 使用：

```text
ControlMask = EXECUTION_SPEED
StateMask   = 0
```

这不是“放弃控制并恢复系统默认”，而是显式关闭该 throttling。更稳妥的产品语义应该是 Stop 后恢复 system-managed：

```text
ControlMask = 0
StateMask   = 0
```

这是 P1 缺陷。

### BUG-005：所有 ResourceGovernor 错误被吞掉

存在多个：

```csharp
catch { }
```

同时没有检查 `SetProcessInformation()` 返回值。

后果：UI 可能打印：

```text
已开启：CPU 低优先级 + EcoQoS 节能
```

实际上 API 已失败。

这是 P0/P1 级可观测性问题。

### BUG-006：IsAlive 仅检查 HWND 存在

当前：

```csharp
public bool IsAlive => Native.IsWindow(Handle);
```

Windows HWND 可能复用，因此仅 `IsWindow()` 不足以证明该窗口仍属于原 Overwatch PID。

应验证：

- Process 未退出；
- HWND 存在；
- `GetWindowThreadProcessId(hwnd)` 仍等于保存的 PID；
- 可选：Process StartTime/实例标识仍一致。

这是 P1 缺陷。

### BUG-007：扩展键 lParam 不完整

当前 `PulseRunner.Key()` 未设置 bit 24 extended-key flag。

应补齐：

- arrows；
- Insert/Delete；
- Home/End；
- PageUp/PageDown；
- NumPad Divide；
- NumPad Enter；
- Right Ctrl；
- Right Alt；
- 其他 Microsoft 定义的 extended key。

Shift 当前不受影响，但 KeyNames 已暴露方向键，所以公共能力与编码实现不一致。

这是 P2 缺陷。

### BUG-008：WindowPlacement 没有处理失败返回值

`GetWindowRect()` 与 `SetWindowPos()` 返回值均未校验。

后果是：

- UI 可能提示“已移出屏幕”；
- 实际操作失败；
- `IsOffscreen` 仍被设置 true；
- 后续状态进一步失真。

这是 P1 缺陷。

### BUG-009：文档测试数量过期

`docs/refactor-plan.md` 仍写 38/38 tests，但当前源码静态统计为：

- 8 个 `[Fact]`
- 3 个 `[Theory]`
- 27 个 `[InlineData]`
- 总计 35 个测试 case

属于仓库可信度问题。

### BUG-010：项目目标框架未声明 Windows-only

当前所有项目：

```xml
<TargetFramework>net8.0</TargetFramework>
```

但核心功能强依赖 `user32.dll/kernel32.dll`。

建议改为：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

并启用：

```xml
<Nullable>enable</Nullable>
```

逐步清理 nullability。

---

# 4. 产品目标

## 4.1 v1.0 目标

v1.0 必须做到：

1. 自动可靠识别 Overwatch 目标进程与正确主窗口；
2. 后台按键能力保持当前已验证行为；
3. 按键脉冲不会影响用户当前正在操作的前台应用；
4. 长时间运行时可检测目标进程死亡与重启；
5. 目标重建后可重新绑定并恢复完整运行策略；
6. 应用停止/退出后恢复原资源优先级与窗口位置；
7. 所有关键 Win32 调用必须报告结果，不允许静默吞错；
8. UI 能展示“实际应用成功/失败”，而不是只展示用户意图；
9. GPU 后台限帧至少有明确检测/配置指引；
10. 提供可运行测试与实机验收清单；
11. 仓库具备 README、LICENSE 决策、发布构建脚本和基础 CI；
12. 代码中不得存在内存注入、Hook、驱动级模拟、反作弊规避能力。

## 4.2 v1.0 非目标

以下功能不进入 v1：

- 自动登录 Battle.net；
- 自动创建/加入自定义房间；
- 图像识别 UI；
- OCR；
- 自动判断游戏内当前英雄/地图/战令等级；
- 自动检测是否被游戏系统标记 AFK；
- 根据游戏状态动态改变行为；
- 自动点击菜单；
- 读游戏内存；
- 网络包分析；
- 反作弊绕过；
- 多账号并发控制；
- 云端远程控制；
- Web 控制台；
- 手机控制；
- 自动更新器。

---

# 5. 产品范围与优先级

采用 MoSCoW：

## Must Have

- 强健 Target Identity；
- Session 状态机；
- 正确 Attach/Detach/Reattach；
- 输入 Pulse 行为保持；
- ResourceGovernor 状态快照/恢复；
- EcoQoS 正确恢复 system-managed；
- 所有 native call 结果结构化；
- WindowPlacement 正确快照/恢复；
- 游戏重启自动重连；
- 目标窗口 PID 验证；
- 日志；
- CLI 基本操作；
- 自动化测试；
- README；
- Windows-only target framework。

## Should Have

- Windows 托盘 UI；
- GPU 后台限帧配置检测；
- NVIDIA 环境检测；
- 运行统计；
- 配置文件；
- 启动时安全恢复上次异常退出状态；
- 单实例运行；
- 通知气泡。

## Could Have

- NVIDIA NVAPI DRS 自动配置；
- AMD 对应驱动策略研究；
- CPU Job Object 实验模式；
- 多 Profile；
- Windows 开机启动；
- 日志导出 zip。

## Won't Have（明确不做）

- anti-ban；
- undetected mode；
- stealth；
- process hiding；
- memory injection；
- DLL injection；
- kernel driver；
- game hook；
- input driver spoofing；
- anti-cheat bypass。

---

# 6. 用户体验设计

## 6.1 主流程

```text
启动 OwHelper
   ↓
检测 Overwatch.exe
   ↓
未找到 → WaitingForTarget
找到 → Ready
   ↓
用户 Start
   ↓
保存目标原始资源状态
   ↓
应用 Background Policy
   ↓
Running
   ↓
按配置周期向目标 HWND 发 Pulse
   ↓
如果 OW 前台 → Skip 本轮
   ↓
如果 OW 退出 → Reattaching
   ↓
新进程出现 → 绑定新 PID/HWND → 重新 Apply Policy → Running
   ↓
用户 Stop / Quit
   ↓
停止调度
   ↓
恢复窗口位置
   ↓
恢复进程原 Priority / Power policy
   ↓
Ready / Exit
```

## 6.2 CLI v1

保留现有 CLI，新增状态可观测能力。

建议：

```text
=== OW Helper ===
Version: 1.0.0

Target
  Process : Overwatch.exe
  PID     : 18324
  HWND    : 0x00050A42
  Window  : 1920x1080 / alive

Input
  Keys    : Shift
  Hold    : 200 ms
  Interval: 30 s
  Mode    : Background window message

Resource policy
  Priority: Normal -> BelowNormal       [Applied]
  EcoQoS  : SystemManaged -> Enabled    [Applied]
  GPU cap : NVIDIA Background Max FPS   [Not configured]

State: READY

Space  Start/Stop
M      Move window offscreen/restore
R      Reattach
+/-    Interval
S      Status
L      Show recent logs
Q      Quit
```

运行后：

```text
State: RUNNING
[19:31:00] pulse #1 OK, 4/4 messages
[19:31:30] pulse #2 skipped: target is foreground
[19:32:01] pulse #3 OK, 4/4 messages
```

如果资源应用失败：

```text
Resource policy
  Priority: Apply failed, Win32=5 Access denied
  EcoQoS  : Applied

WARNING: resource policy partially applied.
Input automation can continue.
```

不得再显示笼统的“已开启 CPU 低优先级 + EcoQoS”，除非两项都真的成功。

---

# 7. Session 状态机设计

当前 `Running => loopTask != null && !loopTask.IsCompleted` 太弱，无法描述真实状态。

新版本必须引入显式状态：

```csharp
public enum SessionState
{
    Detached,
    WaitingForTarget,
    Ready,
    Starting,
    Running,
    Reattaching,
    Stopping,
    Faulted,
    Disposed
}
```

可选增加：

```text
PausedForeground
```

但推荐把“前台跳过”视为 Running 的一次调度结果，而不是 Session 状态，避免状态爆炸。

## 7.1 状态迁移

```text
Detached
  └─Attach success→ Ready
  └─Attach miss→ WaitingForTarget

WaitingForTarget
  └─target found→ Ready
  └─Dispose→ Disposed

Ready
  └─Start→ Starting
  └─Reattach→ Ready/WaitingForTarget
  └─Dispose→ Disposed

Starting
  └─policy apply success/partial→ Running
  └─critical error→ Faulted

Running
  └─Stop→ Stopping
  └─target invalid→ Reattaching
  └─fatal scheduler error→ Faulted

Reattaching
  └─new target found→ Apply policy→ Running
  └─timeout/manual stop→ Stopping

Stopping
  └─scheduler stopped + restore done→ Ready/Detached

Faulted
  └─Reset→ Ready/Detached
  └─Dispose→ Disposed
```

## 7.2 并发要求

所有 Session 状态变更必须串行化。

推荐二选一：

### 方案 A：`SemaphoreSlim` 状态锁

适合当前项目规模。

- `StartAsync`
- `StopAsync`
- `AttachAsync`
- `DisposeAsync`

所有写操作进入同一 `SemaphoreSlim(1,1)`。

### 方案 B：单线程 Actor/Event Loop

架构更纯，但对当前工具过度设计。

v1 推荐 A。

不得继续让 UI 线程直接修改 `target/governor/placement/cts/loopTask` 多个字段而没有原子边界。

---

# 8. Target Identity 设计

## 8.1 问题

`HWND` 本身不是稳定身份。

目标身份应至少包含：

```csharp
public sealed record TargetIdentity(
    int Pid,
    DateTime ProcessStartTimeUtc,
    nint Hwnd,
    string ProcessName,
    string WindowClass,
    string WindowTitle);
```

## 8.2 IsAlive 规则

判定 target 有效必须同时满足：

1. `Process.HasExited == false`；
2. 当前 `process.Id == TargetIdentity.Pid`；
3. 可选校验 Process StartTime 未变化；
4. `IsWindow(hwnd) == true`；
5. `GetWindowThreadProcessId(hwnd) == TargetIdentity.Pid`。

如果第 4 成功但第 5 不一致，必须视为旧 HWND 已失效，立即停止向该句柄发送输入。

## 8.3 窗口选择规则

保持当前“最大面积顶层窗口”作为默认策略，但增加过滤：

- PID 必须匹配；
- Width > 0；
- Height > 0；
- 优先 Visible 顶层窗口；
- 如果最大窗口 minimized，仍可作为候选；
- 如果存在多个相同面积窗口，可优先 Title 非空或已知 Class。

不要把窗口标题字符串写死为唯一判断条件，以免语言/版本变化。

---

# 9. Background Input Engine

## 9.1 行为契约

默认 Pulse：

```text
WM_SETFOCUS
wait 50 ms
KEYDOWN keys in order
hold 200 ms
KEYUP keys in reverse order
WM_KILLFOCUS
```

保留：

- 多键支持；
- `SendActivateApp` 可选；
- `SendActivate` 可选；
- `SendFocus` 可选。

默认生产 profile 仍使用当前已实机验证的 focus recipe。

## 9.2 输入不能抢真实前台

禁止在生产默认路径调用：

- `SetForegroundWindow`
- `SwitchToThisWindow`
- 全局 `SendInput`
- 模拟真实全局按键的 API

诊断工具可以单独试验，但不得成为默认生产路径。

## 9.3 Extended Key 支持

引入：

```csharp
public readonly record struct KeySpec(
    int VirtualKey,
    bool IsExtended,
    string DisplayName);
```

`KeyNames.Parse()` 不再只返回 int。

或者保留 public int API，同时内部：

```csharp
static bool IsExtendedVirtualKey(int vk)
```

构造 lParam：

```text
bits 0-15  = repeat 1
bits 16-23 = scan code
bit 24     = extended
bit 29     = context (按消息类型)
bit 30     = previous state for up
bit 31     = transition for up
```

## 9.4 失败策略

`PulseResult` 应升级为：

```csharp
public sealed record PulseResult(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    IReadOnlyList<MessageOutcome> Messages,
    PulseStatus Status,
    string? FailureReason);
```

状态：

```text
Succeeded
PartiallyFailed
TargetInvalid
SkippedForeground
Cancelled
```

如果连续 N 次出现 native message failure：

- 记录 warning；
- 重新验证 Target Identity；
- 如果 target invalid → Reattach；
- 如果 target valid 但权限错误 → 进入 degraded state，继续有限重试；
- 不要提示用户“管理员运行”作为唯一结论，应把实际 Win32 error 显示出来。

## 9.5 调度间隔

产品必须支持固定间隔。

默认：30 秒。

允许范围：5-300 秒。

当前 ±15% jitter 不应作为“绕检测”功能存在。建议 v1：

- 默认关闭 jitter；
- 如果保留，只能作为普通 Scheduler jitter，用于避免定时任务同一时刻竞争资源；
- 配置名不得使用 `humanize`、`anti-detect`、`anti-ban` 等语义；
- README 不宣传其规避检测用途。

---

# 10. ResourceGovernor v2

这是 v1 的核心重构模块。

## 10.1 目标

ResourceGovernor 必须具备：

- Snapshot；
- Apply；
- Verify/Report；
- Restore；
- Idempotency；
- Partial failure handling。

建议 API：

```csharp
public interface IResourceGovernor : IAsyncDisposable
{
    ResourceSnapshot Snapshot { get; }
    Task<ResourceApplyResult> ApplyAsync(ResourcePolicy policy, CancellationToken ct);
    Task<ResourceRestoreResult> RestoreAsync(CancellationToken ct);
}
```

## 10.2 Snapshot

至少保存：

```csharp
public sealed record ResourceSnapshot(
    ProcessPriorityClass PriorityClass,
    bool PriorityCaptured,
    PowerThrottleSnapshot PowerThrottle);
```

如果 PowerThrottle 当前状态无法在所有系统可靠读取，则定义：

```text
Unknown
SystemManaged
ForcedEcoQoS
ForcedHighQoS
```

恢复策略必须明确：

- 若成功读取原状态 → 恢复原状态；
- 若无法可靠读取 → Restore 时使用 Microsoft 文档定义的 system-managed：`ControlMask=0, StateMask=0`；
- 不要强制 HighQoS。

## 10.3 Priority Apply

默认策略：

```text
BelowNormal
```

Apply 前保存原值。

失败不能吞掉。

结果结构：

```csharp
public sealed record OperationResult(
    string Name,
    bool Success,
    int? NativeError,
    string? Message);
```

## 10.4 EcoQoS Apply

使用：

```text
Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION
ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
```

不要魔法数字：

```csharp
const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
```

## 10.5 EcoQoS Restore

首选恢复 snapshot。

无法 snapshot 时：

```text
Version = CURRENT
ControlMask = 0
StateMask = 0
```

恢复系统管理。

## 10.6 Native memory safety

当前代码手工：

```csharp
Marshal.AllocHGlobal
Marshal.StructureToPtr
SetProcessInformation
Marshal.FreeHGlobal
```

必须 `try/finally`，避免调用异常时内存泄漏。

更推荐直接声明强类型 P/Invoke：

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetProcessInformation(
    IntPtr hProcess,
    PROCESS_INFORMATION_CLASS infoClass,
    ref PROCESS_POWER_THROTTLING_STATE info,
    uint size);
```

避免不必要的 unmanaged allocation。

## 10.7 不默认使用 CPU Affinity

v1 不改变 CPU Affinity。

理由：

- 可能影响游戏内部线程模型；
- 与 Intel P/E Core 及 Windows scheduler 交互复杂；
- EcoQoS 已经允许系统优先使用能效策略；
- 反作弊/权限程序可能阻止 affinity 修改。

如果以后做，作为 Advanced Experimental。

## 10.8 不默认使用 Job CPU Hard Cap

原因见前文。

若 Phase 3 实验：

- 默认 Off；
- UI 明确 Experimental；
- 只允许 10%-100%；
- 有 watchdog；
- 一旦目标响应超时立刻移除 cap；
- 必须验证进程是否已属于不可嵌套 Job。

---

# 11. GPU Resource Strategy

## 11.1 v1 产品策略

不尝试通过移动窗口到屏幕外来宣称“GPU 已降低”。

`MoveOffscreen` 只属于窗口管理能力，不能作为 GPU 限制能力展示。

v1 UI 必须把 CPU 与 GPU 状态分开：

```text
CPU Policy: Applied
GPU Policy: Not configured
```

## 11.2 NVIDIA 检测

实现一个轻量 `GpuEnvironmentDetector`：

- WMI / DXGI / Registry 检测 NVIDIA GPU；
- 检测 NVIDIA App/Control Panel 是否存在可以作为辅助信息；
- 不要求管理员权限；
- 检测失败不影响核心输入功能。

输出：

```text
GPU Vendor: NVIDIA
Background FPS cap: configuration unknown
Recommendation: 20 FPS
```

## 11.3 用户引导模式（v1 Must/Should）

如果检测到 NVIDIA：

提示用户：

```text
NVIDIA App / NVIDIA Control Panel
→ Graphics / Manage 3D Settings
→ Program Settings: Overwatch.exe
→ Background Application Max Frame Rate
→ 20 FPS
```

工具可以提供：

```text
[Open NVIDIA App]
[Copy setup instructions]
```

如果难以可靠 deep-link，不要做脆弱 UI 自动点击。

## 11.4 NVAPI 自动设置（v1.x Optional）

只有满足以下条件才实现：

1. 使用 NVIDIA 官方 NVAPI；
2. setting ID 来源于 NVIDIA 官方公开 `NvApiDriverSettings.h`；
3. 能确定 Background Application Max Frame Rate 对应的 ID 和值编码；
4. 能读取旧值；
5. 能创建/查找 `Overwatch.exe` 应用 profile；
6. 修改后调用 `NvAPI_DRS_SaveSettings`；
7. Stop/Exit 不一定自动恢复——因为这是持久化驱动 profile，需要与“本次 Session 临时策略”区分；
8. UI 必须让用户明确确认持久化修改。

若官方头文件只公开 `Idle Application Max FPS Limit` / `Frame Rate Limiter` 等名字但无法确认与 Control Panel 当前显示项完全一致，不得猜测 setting ID。

## 11.5 GPU 验收指标

GPU 优化成功标准不是“代码成功调用某 API”，而是实测：

- OW 后台时 FPS 接近配置上限；
- GPU utilization 明显低于无 cap；
- 前台恢复时正常；
- 不导致画面/进程冻结；
- 输入 Pulse 仍正常。

不设置硬性百分比，因为不同 GPU、分辨率、菜单场景差异大。

---

# 12. WindowPlacement v2

## 12.1 状态模型

```csharp
public sealed record WindowPlacementSnapshot(
    nint Hwnd,
    int Pid,
    Rect Rect,
    bool WasMinimized,
    bool Captured);
```

## 12.2 MoveOffscreen

执行前：

1. 验证 Target Identity；
2. `GetWindowRect` 必须成功；
3. 保存 snapshot；
4. 调用 `SetWindowPos`；
5. 检查返回值；
6. 再 `GetWindowRect` 进行 best-effort 验证；
7. 成功后才能 `IsOffscreen=true`。

失败时不得改变状态。

## 12.3 Restore

Restore 必须：

- 只对 snapshot 中同 PID/HWND 的目标执行；
- 如果目标进程已死亡，标记 snapshot stale 并清理；
- 如果 HWND 被复用到其他 PID，绝对不能 SetWindowPos；
- Restore 成功后清除 snapshot。

## 12.4 Reattach 前规则

任何 Reattach 之前：

```text
if old placement is active:
    attempt restore old window
then dispose old target resources
then attach new target
```

如果旧进程已经死亡，Restore 不可执行，但应安全丢弃 snapshot。

---

# 13. Reattach / Game Restart

## 13.1 触发条件

以下任一成立进入 Reattaching：

- Process HasExited；
- IsWindow false；
- HWND PID mismatch；
- 连续 PostMessage 失败 >= 3 且 TargetValidation 失败；
- 用户手工 `r`。

## 13.2 自动重连策略

推荐：

```text
initial delay: 1 s
poll interval: 2 s
max wait: configurable, default unlimited while Running
```

无需指数退避，因为本地进程检测成本低。

用户 Stop 必须立即取消等待。

## 13.3 新目标绑定

找到新目标后完整执行：

1. 创建新 `GameWindow` / TargetIdentity；
2. 创建新 ResourceGovernor；
3. Capture 新进程原始资源状态；
4. 如果 Session 原来是 Running → Apply policy；
5. 窗口 offscreen 状态默认不要自动继承，除非产品明确提供 `KeepOffscreenAcrossRestart=true`；
6. 日志记录 Old PID → New PID；
7. 恢复 Running。

## 13.4 手工 Reattach

如果 Running 状态下用户按 `r`：

产品不能像当前版本直接覆盖字段。

应定义：

```text
Running + manual reattach
→ temporarily stop pulses
→ restore old resources/window placement
→ detach old target
→ discover target
→ capture/apply new target
→ resume scheduler
```

或者简单策略：Running 时禁止 `r`，提示“请先 Stop”。

v1 推荐前者，但若为了降低复杂度，允许先实现后者。

---

# 14. Foreground Safety Policy

保留现有设计：当 Overwatch 是真实前台窗口时，本轮 Pulse 跳过。

原因：

- 用户前台主动游戏时不希望工具额外触发技能；
- 这是简单、明确、低风险的防干扰机制。

新增配置：

```json
{
  "skipWhenTargetForeground": true
}
```

默认 true。

如果 false，UI 必须明确显示：

```text
WARNING: background pulse will also fire while OW is foreground.
```

---

# 15. 配置系统

## 15.1 文件位置

建议：

```text
%LOCALAPPDATA%\OwHelper\config.json
```

日志：

```text
%LOCALAPPDATA%\OwHelper\logs\owhelper-YYYYMMDD.log
```

异常恢复状态：

```text
%LOCALAPPDATA%\OwHelper\runtime-state.json
```

## 15.2 配置示例

```json
{
  "target": {
    "processName": "Overwatch"
  },
  "input": {
    "keys": ["shift"],
    "intervalSeconds": 30,
    "holdMilliseconds": 200,
    "focusWaitMilliseconds": 50,
    "sendFocus": true,
    "sendActivate": false,
    "sendActivateApp": false,
    "skipWhenTargetForeground": true
  },
  "resource": {
    "priority": "BelowNormal",
    "ecoQos": true,
    "gpuBackgroundFpsTarget": 20,
    "gpuPolicyMode": "GuideOnly"
  },
  "window": {
    "allowMoveOffscreen": true,
    "keepOffscreenAcrossRestart": false
  },
  "logging": {
    "level": "Information",
    "retainDays": 7
  }
}
```

## 15.3 配置容错

- 文件不存在 → 自动生成默认值；
- JSON 语法错误 → 不覆盖原文件，启动默认值并报警；
- 未知字段忽略；
- 无效 interval → clamp 或拒绝，并打印原因；
- 未知 key → 不启动自动化，提示具体 key；
- 修改配置不要求 v1 热重载。

---

# 16. 日志与可观测性

## 16.1 日志原则

不允许：

```csharp
catch { }
```

除 Cleanup best-effort 之外，每个 catch 至少 Debug/Warning 记录。

## 16.2 日志事件

至少包含：

```text
APP_START
APP_EXIT
TARGET_FOUND
TARGET_LOST
TARGET_REATTACHED
SESSION_START
SESSION_STOP
PULSE_OK
PULSE_PARTIAL_FAILURE
PULSE_SKIPPED_FOREGROUND
RESOURCE_SNAPSHOT
RESOURCE_APPLY
RESOURCE_APPLY_PARTIAL
RESOURCE_RESTORE
WINDOW_MOVE_OFFSCREEN
WINDOW_RESTORE
NATIVE_ERROR
CONFIG_LOAD_FAILED
```

## 16.3 日志字段

结构化字段：

```text
timestamp
level
event
pid
hwnd
sessionId
pulseIndex
nativeError
operation
elapsedMs
message
```

## 16.4 隐私与数据

本地工具不需要上传 telemetry。

v1 默认：

- 无网络请求；
- 无用户账号采集；
- 无机器指纹；
- 无远程日志。

README 明确说明“本地运行，不上传数据”。

---

# 17. Crash Recovery

## 17.1 为什么需要

正常退出可以 Restore，但：

- `Environment.FailFast`
- 断电
- 进程被 Task Manager Kill
- 系统崩溃

无法保证 finally 执行。

Priority/EcoQoS 本身通常随目标进程生命周期结束或可由系统继续管理，但 WindowPlacement 把游戏搬到屏幕外可能跨 helper 崩溃持续存在，因此需要恢复策略。

## 17.2 runtime-state.json

MoveOffscreen 成功后写入：

```json
{
  "version": 1,
  "pid": 18324,
  "processStartTimeUtc": "2026-09-17T11:00:00Z",
  "hwnd": 328770,
  "originalRect": {
    "left": 100,
    "top": 100,
    "right": 1380,
    "bottom": 820
  },
  "offscreenApplied": true
}
```

Restore 成功后立即清除。

应用启动时：

- 如果 state 文件存在；
- 检查 PID/StartTime/HWND 是否仍对应同一进程；
- 若一致，提示：
  - `发现上次异常退出留下的屏幕外窗口，按 Y 恢复`；
- 不要未经确认移动未知窗口；
- 如果 identity 不一致，删除 stale state。

---

# 18. 权限与完整性级别

Windows UIPI 可能阻止低完整性进程向高完整性窗口发送部分消息。

产品需诊断：

- 当前 OwHelper 是否管理员；
- 目标 Overwatch 是否管理员（可 best-effort）；
- PostMessage 失败时显示 Win32 code。

不要默认要求管理员启动。

原则：

> 最小权限运行；只有确实遇到权限问题时才建议相同完整性级别。

README 提醒：不建议为了“更强控制”长期以管理员权限运行，除非实际需要。

---

# 19. 托盘 UI（Should Have）

在核心稳定后，引入 `OwHelper.Tray`，不把 WinForms/WPF 依赖放进 Core。

推荐技术：

- WinForms NotifyIcon：最简单；
- 不必为了小工具上复杂 MVVM 框架。

托盘菜单：

```text
OW Helper
────────────
Status: Running
Target: PID 18324
Last pulse: 12s ago

Start / Stop
Move Offscreen / Restore
Open Status
Open Logs
Settings
Exit
```

状态颜色：

- Green：Running + target valid；
- Yellow：Waiting/Reattaching/Partial resource policy；
- Red：Faulted；
- Gray：Stopped。

不要提供“隐藏到无法找到”的 stealth 功能。托盘只是正常桌面交互。

---

# 20. 代码架构目标

建议最终：

```text
src/
├── OwHelper.Core/
│   ├── Interop/
│   │   ├── Native.User32.cs
│   │   ├── Native.Kernel32.cs
│   │   └── NativeTypes.cs
│   ├── Targeting/
│   │   ├── GameWindow.cs
│   │   ├── TargetIdentity.cs
│   │   ├── TargetValidator.cs
│   │   └── WindowDiscovery.cs
│   ├── Input/
│   │   ├── KeyNames.cs
│   │   ├── KeySpec.cs
│   │   ├── PulseRecipe.cs
│   │   ├── PulseRunner.cs
│   │   └── PulseResult.cs
│   ├── Resources/
│   │   ├── ResourceGovernor.cs
│   │   ├── ResourcePolicy.cs
│   │   ├── ResourceSnapshot.cs
│   │   └── ResourceResult.cs
│   ├── Windowing/
│   │   ├── WindowPlacement.cs
│   │   └── WindowPlacementSnapshot.cs
│   └── Diagnostics/
│       └── OperationResult.cs
├── OwHelper/
│   ├── Program.cs
│   ├── Session.cs
│   ├── SessionState.cs
│   ├── AppConfig.cs
│   └── ConsoleUi.cs
├── OwHelper.Tray/               # Phase 2
└── BgKeyProbe/

tests/
├── OwHelper.Core.Tests/
└── OwHelper.App.Tests/
```

## 20.1 Core 原则

Core：

- 不写 Console；
- 不依赖 WinForms/WPF；
- 不知道“战令”“挂机”等产品词；
- 不做反作弊逻辑；
- 只表达 Windows 能力。

## 20.2 App 原则

App：

- 负责 Session；
- 负责用户配置；
- 负责日志；
- 负责策略编排；
- 不直接 P/Invoke。

---

# 21. 接口建议

## 21.1 ITargetLocator

```csharp
public interface ITargetLocator
{
    TargetDiscoveryResult Find(string processName);
}
```

## 21.2 ITargetValidator

```csharp
public interface ITargetValidator
{
    TargetValidationResult Validate(TargetIdentity target);
}
```

## 21.3 IPulseSender

```csharp
public interface IPulseSender
{
    PulseResult Execute(TargetIdentity target, PulseRecipe recipe);
}
```

## 21.4 IResourceGovernor

前文已述。

## 21.5 IWindowPlacementController

```csharp
public interface IWindowPlacementController
{
    WindowMoveResult MoveOffscreen(TargetIdentity target);
    WindowMoveResult Restore(TargetIdentity target);
    bool HasActivePlacement { get; }
}
```

不要为了“接口化”把所有小类都抽象；只有 Session 测试需要替换的边界才抽接口。

---

# 22. 错误分类

定义：

```text
Recoverable
Degraded
Fatal
```

## Recoverable

- 暂时找不到 OW；
- OW 重启；
- 单次 PostMessage 失败；
- 窗口最小化；
- GPU 配置未知。

行为：等待/重试，不结束进程。

## Degraded

- Priority 设置失败但输入可用；
- EcoQoS 失败但输入可用；
- Offscreen 失败；
- 日志文件写入失败。

行为：继续核心能力，明显警告。

## Fatal

- Session 内部状态损坏；
- 配置核心字段不可解析且无法默认；
- Scheduler 未处理异常持续崩溃；
- Dispose 之后仍被调用。

行为：停止 Session，恢复可恢复状态，进入 Faulted。

---

# 23. 测试需求

当前 FakeWindow 测试应保留并扩展。

## 23.1 PulseRunner 单元/集成测试

必须：

1. 默认消息顺序；
2. 多键 Down 顺序；
3. 多键 Up 逆序；
4. ScanCode；
5. KeyUp bit 30/31；
6. Extended key bit 24；
7. NoFocus；
8. Activate；
9. Invalid HWND 失败结果；
10. Cancel 语义（若引入 async）。

## 23.2 KeyNames

覆盖：

- shift/ctrl/alt；
- A-Z；
- 0-9；
- F1-F12；
- arrows；
- aliases；
- unknown key exception；
- extended flag。

## 23.3 ResourceGovernor

不能再假定测试前 Priority 一定 Normal。

测试：

```text
capture original
apply BelowNormal
restore
assert == original
```

EcoQoS：

- mock native layer，断言 Apply mask；
- Restore 默认应 ControlMask=0/StateMask=0；
- SetProcessInformation false 时返回错误而不是成功；
- Apply 二次调用幂等；
- Restore 二次调用幂等；
- Process exited 时安全失败。

## 23.4 WindowPlacement

需要引入可 mock Native 边界或 FakeWindow：

- Move 成功保存 Rect；
- Move 失败不改 IsOffscreen；
- Restore 恢复；
- PID mismatch 拒绝；
- stale target 不操作；
- Reattach 前恢复。

## 23.5 Session 测试（当前最缺）

新增 `OwHelper.App.Tests`。

至少：

### Case S01

Ready → Start → Running

断言：

- policy Apply 一次；
- scheduler 启动。

### S02

Running → Stop

断言：

- scheduler cancel；
- Window restore；
- Resource restore；
- State Ready。

### S03

Start twice

断言：第二次 no-op，不重复 Apply。

### S04

Stop twice

断言：幂等。

### S05

Target dies while Running

断言：

- State Reattaching；
- 找到新 target；
- 对新 target 重新 Apply resource policy；
- 返回 Running。

### S06

Running + target foreground

断言：Pulse skipped，但 scheduler 继续。

### S07

MoveOffscreen → Manual Reattach

断言：旧 placement 被恢复或操作被拒绝；绝不能丢 snapshot。

### S08

Pulse failures 3 times + invalid target

断言：进入 Reattach。

### S09

Priority apply failure + EcoQoS success

断言：Running allowed，状态 degraded。

### S10

Dispose from Running

断言：完整 cleanup，仅一次。

## 23.6 架构测试

保留：

- app assemblies 无 `DllImport`；
- Core 无 UI 依赖。

新增：

- `OwHelper.Core` 不引用 `OwHelper`；
- Core 不引用 WinForms/WPF；
- 禁止字符串/API：
  - `WriteProcessMemory`
  - `ReadProcessMemory`
  - `CreateRemoteThread`
  - `SetWindowsHookEx`

可用简单源码扫描 CI gate。

---

# 24. 实机验收矩阵

单元测试无法证明游戏接受消息，所以必须保留实机测试。

## ENV-01

Windows 11 + NVIDIA GPU + OW 窗口模式。

## ENV-02

Windows 11 + OW 最小化。

## ENV-03

Windows 11 + Chrome/VS Code 前台。

验收：

### A. 后台输入

- OW 后台；
- Chrome 前台；
- 运行 10 次 Pulse；
- 目标行为 10/10 生效；
- Chrome 不收到 Shift 副作用。

### B. 前台跳过

- OW 切前台；
- 到达下一次调度；
- 不触发 Pulse；
- 日志为 SkippedForeground。

### C. 恢复后台

- 再切 Chrome；
- 下一轮 Pulse 恢复。

### D. OW 重启

- Running 时关闭 OW；
- Helper 不崩溃；
- 状态显示 Reattaching；
- 重开 OW；
- 自动找到新 PID；
- Resource policy 对新 PID 重新 Apply；
- Pulse 恢复。

### E. Window Offscreen

- MoveOffscreen；
- Manual reattach/Stop/Exit 任一操作后窗口可恢复；
- 不允许留下屏幕外窗口。

### F. Resource Restore

人为先把 OW Priority 设置成 `AboveNormal`（测试环境）；

- Start → BelowNormal；
- Stop → 应恢复 AboveNormal，而非 Normal。

### G. EcoQoS

- Start 时 Apply 成功有明确结果；
- Stop 时回 SystemManaged；
- API 失败时 UI 不虚报成功。

### H. GPU

配置 NVIDIA Background Max FPS 20：

- 后台 FPS 接近 20；
- GPU 占用相较未限制下降；
- 前台恢复正常游戏帧率（取决于 driver setting 语义和前台配置）。

---

# 25. 性能指标

OwHelper 自身必须轻量。

目标：

- Idle CPU：< 0.5%（典型桌面 CPU，非硬性 SLA）；
- Working Set：< 100 MB；
- 不使用高频 polling；
- target alive 检测周期 >= 1s；
- scheduler 等待使用 `Task.Delay`，禁止 busy loop；
- 日志异步/低频；
- 不创建持续高分辨率 timer。

对 Overwatch 的资源改善不设统一百分比 SLA，因为硬件差异大，但必须记录前后对比方法。

---

# 26. 安全与稳定性约束

## 26.1 禁止危险资源恢复

不能在不确认 PID/HWND identity 的情况下恢复窗口。

## 26.2 进程句柄

`.NET Process` 可能延迟打开 handle。使用后应 Dispose 旧 Process 对象。

Session Reattach 时：

- Restore old policy；
- Dispose governor；
- Dispose old Process；
- 再绑定新 Process。

## 26.3 P/Invoke

所有 SetLastError=true 的 API 失败后立刻捕获 `Marshal.GetLastWin32Error()`，不要在中间调用其他 native API。

## 26.4 Unmanaged memory

禁止裸 `AllocHGlobal` 无 finally。

## 26.5 Exit

处理：

- Console.CancelKeyPress；
- ProcessExit；
- GUI FormClosing；
- DisposeAsync。

但不能假设 ProcessExit 中 async cleanup 一定可靠，因此关键恢复应在正常 Stop 路径完成。

---

# 27. 仓库工程化需求

## 27.1 README.md

必须包括：

1. 项目是什么；
2. 支持系统；
3. .NET 版本；
4. Build；
5. Run；
6. CLI 参数；
7. 配置示例；
8. 已验证行为；
9. Known limitations；
10. Resource policy；
11. NVIDIA 后台帧率设置；
12. Troubleshooting；
13. 平台条款风险说明；
14. 明确“不提供 anti-cheat bypass”；
15. License。

## 27.2 LICENSE

仓库是 Public 不等于允许自由复制。

Owner 必须明确选择许可证。

Coding Agent 不应擅自替用户选择法律许可证。

可以创建：

```text
LICENSE-TODO.md
```

或在 README 标明“License not yet selected”，等待 owner 决策。

如果用户后续明确选 MIT，再添加 MIT。

## 27.3 CI

GitHub Actions：

```yaml
windows-latest
setup-dotnet 8.x
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

只跑 Windows，因为项目 Windows-only。

## 27.4 Build

所有 csproj：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Nullable 可以分阶段启用；如果一次启用导致大量改动，可单独 commit。

## 27.5 Publish

提供 PowerShell：

```text
scripts/publish.ps1
```

生成：

```text
artifacts/OwHelper-win-x64/
```

推荐：

```text
-r win-x64
--self-contained false
```

第一阶段不要追求 single-file/AOT，先确保可诊断。

---

# 28. 版本路线图

## Phase 0 — Correctness Hotfix

目标：先修现有 P0。

内容：

1. Running 状态禁止/安全处理 Reattach；
2. Reattach 后重新 Apply governor；
3. WindowPlacement 不再因 Attach 丢失；
4. Native error 不再吞；
5. SetProcessInformation 返回值检查；
6. Priority 保存/恢复；
7. EcoQoS 恢复 system-managed。

验收：所有现有测试 + 新 Session tests。

## Phase 1 — Lifecycle Hardening

内容：

- TargetIdentity；
- PID/HWND validate；
- SessionState；
- async Start/Stop；
- 完整 Reattach；
- Resource result；
- WindowPlacement result；
- structured logging；
- config.json。

## Phase 2 — Productization

内容：

- Tray UI；
- crash recovery state；
- NVIDIA GPU detection；
- setup guidance；
- README；
- CI；
- release publish script。

## Phase 3 — Optional GPU Automation Research

内容：

- NVAPI DRS spike；
- 官方 setting ID 验证；
- per-app profile snapshot；
- apply/restore proof；
- 不进入默认路径，除非实测稳定。

## Phase 4 — Experimental Resource Controls

可选：

- CPU Job Object cap；
- affinity/cpu sets；
- metrics UI。

必须默认关闭。

---

# 29. Coding Agent 实施顺序（建议按 commit）

要求 Coding Agent 不做“大爆炸式重写”。每个 commit 都可 build/test。

## Commit 1 — project hygiene

- net8.0 → net8.0-windows；
- 修 docs 35 tests；
- README skeleton；
- 不改运行行为。

## Commit 2 — Native result hardening

- 强类型 SetProcessInformation；
- constants 命名；
- OperationResult；
- 不再 swallow errors。

## Commit 3 — ResourceGovernor snapshot/restore

- capture original Priority；
- Apply result；
- Restore original Priority；
- EcoQoS restore system-managed；
- tests。

## Commit 4 — TargetIdentity validation

- PID + hwnd + StartTime；
- IsAlive 强化；
- HWND PID mismatch test。

## Commit 5 — WindowPlacement hardening

- operation results；
- identity guard；
- Move/Restore tests；
- stale behavior。

## Commit 6 — Session state machine

- explicit state；
- serialized Start/Stop/Reattach；
- Session tests。

## Commit 7 — reattach semantics

- target death；
- new target resource re-apply；
- running manual `r` safety；
- old resource/placement cleanup。

## Commit 8 — extended keys

- KeySpec；
- bit24；
- tests。

## Commit 9 — config + logs

- local appdata；
- config validation；
- structured logs。

## Commit 10 — crash recovery placement

- runtime-state.json；
- startup recovery prompt；
- tests。

## Commit 11 — NVIDIA guidance

- GPU vendor detect；
- status；
- instructions；
- 不自动修改 driver profile。

## Commit 12 — CI/publish

- GitHub Actions；
- scripts/publish.ps1；
- version display。

## Commit 13 — Tray UI（独立可选）

- 新项目；
- 复用 Session service；
- 不复制 Core 逻辑。

---

# 30. 验收定义（Definition of Done）

v1 只有同时满足以下条件才算完成：

## Build

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

全部成功，0 error。

Warnings：

- 目标 0；
- 如暂时不是 0，必须在 PR 说明。

## Functional

- 后台 Shift 实机连续 30 分钟有效；
- 前台 Chrome 可正常使用；
- OW 前台时能跳过；
- OW 重启自动恢复；
- Resource policy 新 PID 自动 reapply；
- Stop 恢复原 Priority；
- Stop 恢复 system-managed power policy；
- m → Stop/Exit 后窗口恢复；
- r 不会丢 placement；
- invalid HWND 不会向其他 PID 发消息。

## Reliability

至少 4 小时 soak test：

- Helper 不崩溃；
- 内存无持续单调增长；
- pulse scheduler 无明显 drift；
- 日志可解释所有跳过/失败；
- 不出现窗口无法恢复。

## Repository

- README；
- CI；
- Publish script；
- docs 与测试数量一致；
- LICENSE 状态明确；
- no binaries committed unless intentional release artifact policy documented。

## Safety boundary

源码扫描不得出现：

```text
WriteProcessMemory
ReadProcessMemory
VirtualAllocEx
CreateRemoteThread
SetWindowsHookEx
NtWriteVirtualMemory
kernel driver install
```

如果未来真的因为其他正当功能需要其中某项，必须重新做架构审查，不得静默引入。

---

# 31. 对当前代码的直接修改建议

## 31.1 `ResourceGovernor.cs`

当前：

```csharp
readonly Process process;

public void Apply()
{
    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    SetEcoQoS(true);
}
```

建议目标：

```csharp
public sealed class ResourceGovernor : IDisposable
{
    private readonly Process process;
    private readonly ProcessPriorityClass originalPriority;
    private bool priorityCaptured;
    private bool applied;

    public ResourceApplyResult Apply(ResourcePolicy policy) { ... }
    public ResourceRestoreResult Restore() { ... }
}
```

Restore 的 power policy 不再调用 `SetEcoQoS(false)`，而是 `ResetPowerThrottlingToSystemManaged()`。

## 31.2 `GameWindow.cs`

当前：

```csharp
public bool IsAlive => Native.IsWindow(Handle);
```

改：

```csharp
public TargetValidationResult Validate()
{
    if (Process.HasExited) ...
    if (!Native.IsWindow(Handle)) ...
    Native.GetWindowThreadProcessId(Handle, out var pid);
    if (pid != Pid) ...
    return Valid;
}
```

不要在 property getter 中做过多可能抛异常的系统调用。

## 31.3 `Session.Attach()`

禁止继续直接覆盖字段。

实现：

```csharp
private async Task<AttachResult> ReplaceTargetAsync(...)
```

顺序：

```text
pause scheduler if needed
restore old placement
restore old governor
dispose old target resources
find new target
create target context
capture resource snapshot
apply policy if was running
resume
```

## 31.4 `WindowPlacement.cs`

不再用 bool 单字段表达所有状态。

引入：

```text
None
Captured
Offscreen
RestoreFailed
Stale
```

或者至少使用 operation result。

## 31.5 `Program.cs`

把 Console command parsing 从 Session 逻辑中保持分离。

最终：

```csharp
await app.RunAsync();
```

而非所有逻辑在 Main while(true) 中扩张。

---

# 32. Coding Agent 约束 Prompt（可直接附在任务中）

下面这一段可直接作为 Coding Agent 的实现指令：

```text
You are modifying the existing ow_helper repository. Do not rewrite the project from scratch.

Primary goal:
Turn the current working background-input PoC into a reliable Windows-only application with correct lifecycle, resource-state restoration, target reattachment, error reporting, and tests.

Hard constraints:
1. Preserve the currently verified PostMessage-based background pulse behavior.
2. Do not introduce DLL injection, game memory access, API hooks, kernel drivers, process hiding, anti-cheat bypasses, or detection-evasion logic.
3. App projects must not contain DllImport. Keep native interop centralized in Core.
4. Every change must keep the solution buildable and testable.
5. Make small commits / logically isolated changes.
6. Never swallow native errors silently except best-effort cleanup; return structured results and log failures.
7. Do not claim an operation succeeded unless the underlying Win32 call succeeded.
8. Restore original process priority, not hard-coded Normal.
9. Restore PowerThrottling to system-managed semantics (ControlMask=0, StateMask=0) unless a reliably captured original state is available.
10. Reattach must reapply the resource policy to the new process.
11. Never send messages to an HWND unless GetWindowThreadProcessId still matches the stored target PID.
12. Reattach must not lose an active WindowPlacement snapshot.
13. Implement extended-key bit 24 for applicable keys.
14. Target net8.0-windows.
15. Add/maintain automated tests for Session lifecycle and resource restoration.

Implementation order:
- native/result hardening
- resource snapshot/restore
- target identity validation
- window placement hardening
- explicit Session state machine
- reattach semantics
- extended key handling
- config/logging
- crash recovery
- NVIDIA guidance (do not guess undocumented driver settings)
- CI/release tooling

Before finishing:
- dotnet restore
- dotnet build -c Release
- dotnet test -c Release
- summarize changed behavior
- list known remaining risks
- provide a manual real-game smoke-test checklist
```

---

# 33. 需要 Coding Agent 特别避免的“看似优化”

## 33.1 不要把 `PostMessage` 换成 SendInput

会破坏最核心的“用户还能正常操作前台应用”的要求。

## 33.2 不要为了成功率引入 SetForegroundWindow

这会抢用户焦点。

## 33.3 不要通过 DLL 注入解决消息兼容问题

这违反项目边界，也显著提高风险。

## 33.4 不要用 `Process.Kill` 做任何恢复

工具不应该控制游戏生命周期。

## 33.5 不要把窗口 `Hide`/`ShowWindow(SW_HIDE)` 当成默认策略

隐藏窗口可能改变游戏内部渲染/消息行为。当前已验证的是后台/最小化/移出屏幕组合，应逐个实测，不要无依据改变。

## 33.6 不要把 CPU Priority 设为 Idle

BelowNormal 是更保守的默认值。

## 33.7 不要默认 Hard Cap CPU 10%

可能导致目标逻辑/网络运行不稳定。

## 33.8 不要在 README 宣传“防封”“不检测”

无法证明，也不应成为产品目标。

---

# 34. 最终推荐产品形态

长期看，OW Helper 应形成两层：

```text
┌─────────────────────────────┐
│ OwHelper.Tray / Console UI  │
│                             │
│ Start / Stop                │
│ Target status               │
│ Resource status             │
│ Logs                        │
│ Settings                    │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│ OwHelper Application Layer  │
│ Session state machine       │
│ Scheduler                   │
│ Config                      │
│ Recovery                    │
└──────────────┬──────────────┘
               │
┌──────────────▼──────────────┐
│ OwHelper.Core               │
│ Target discovery/validation │
│ PostMessage pulse engine    │
│ Resource governor           │
│ Window placement            │
│ Win32 interop               │
└─────────────────────────────┘
```

它应该是一个“可解释”的工具：用户随时能看懂现在对游戏做了什么、哪些成功、哪些失败、停止后恢复了什么。

这一点比继续堆功能更重要。

---

# 35. 最终优先级结论

如果 Coding Agent 只能做有限工作，按下面顺序：

**P0：**

1. 修 `Attach()` 丢 placement；
2. 修重启后 governor 不 reapply；
3. 不再吞 ResourceGovernor 错误；
4. PID/HWND identity 校验；
5. Stop 完整恢复。

**P1：**

6. 保存原 Priority；
7. EcoQoS 恢复 system-managed；
8. WindowPlacement 结果校验；
9. Session 状态机；
10. Session tests。

**P2：**

11. extended key；
12. config/logging；
13. crash recovery；
14. net8.0-windows；
15. README/CI/publish。

**P3：**

16. Tray UI；
17. NVIDIA detection/guidance；
18. 官方 NVAPI DRS 可行性验证。

不要反过来先做漂亮 GUI。当前最值钱的是让生命周期和恢复语义正确。

---

# 36. 参考资料

## Microsoft

1. SetProcessInformation  
   https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation

2. PROCESS_POWER_THROTTLING_STATE  
   https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-process_power_throttling_state

3. SetPriorityClass  
   https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass

4. WM_KEYDOWN  
   https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-keydown

5. Job Object CPU Rate Control  
   https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information

## NVIDIA

6. Manage 3D Settings / Background Application Max Frame Rate  
   https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nv3d/Manage_3D_Settings_%28reference%29.htm

7. NVAPI DRS API  
   https://docs.nvidia.com/nvapi/group__drsapi.html

8. NVIDIA NVAPI Driver Settings Header  
   https://github.com/NVIDIA/nvapi/blob/main/NvApiDriverSettings.h

## Blizzard

9. Blizzard End User License Agreement  
   https://www.blizzard.com/en-us/legal/08b946df-660a-40e4-a072-1fbde65173b1/blizzard-end-user-license-agreement

10. Blizzard Anti-Cheating Agreement  
    https://www.blizzard.com/en-us/legal/cd5930c0-2784-420c-a23d-1e0d6ff8599b/anti-cheating-agreement

---

# 37. 文档结论

`ow_helper` 不需要推倒重做。

现有最难的部分——“后台窗口仍能通过定向消息收到按键，而用户可以继续使用前台电脑”——已经被验证。下一阶段的主要工作是把它从“能工作”升级为“状态正确、失败可见、退出可恢复、目标重启可自愈、资源策略可信”。

从工程投入收益比看，最重要的不是增加更多自动化能力，而是修复 Session 生命周期、ResourceGovernor 恢复语义、Target Identity 与 WindowPlacement 这些基础问题。

只要 Phase 0 + Phase 1 完成，这个项目的稳定性会有一个明显跃升；Phase 2 再做托盘 UI 和 GPU 配置引导，才适合把它视为一个可日常长期使用的 Windows 工具。
