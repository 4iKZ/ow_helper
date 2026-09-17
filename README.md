# OW Helper

Windows 后台输入调度器 + 进程资源控制器：让 Overwatch 2 在**失去焦点/后台**时仍能收到定向按键脉冲，同时用户可以继续正常使用电脑；提供 CPU 优先级 / EcoQoS 节流与窗口移出屏幕能力，并在停止时恢复原状。

提供两个前端（**共用单实例互斥体，二选一运行**）：

- **托盘版 `OwHelper.Tray.exe`**（推荐日常使用）：状态色图标 + 菜单 + 完整状态窗口 + 通知气泡
- **控制台版 `OwHelper.exe`**：无窗口场景 / 调试 / 脚本化

> 本工具只使用常规 Windows API（窗口消息投递 + 进程调度接口）。
> **不包含**任何反作弊绕过、DLL 注入、内存读写、API Hook、内核驱动、进程隐藏或检测规避能力（有测试断言源码与程序集红线）。详见 [安全边界](#安全边界与平台条款)。

## 支持环境

- Windows 11 x64（Windows 10 未验证）
- .NET 8 SDK（构建）/ .NET 8 Desktop Runtime（运行）
- Overwatch 2，建议**窗口模式**

## 仓库结构

```text
src/
├── OwHelper.Core/     # 类库：Win32 互操作、目标发现/校验、脉冲引擎、资源治理、窗口放置、GPU 检测
├── OwHelper.App/      # 类库：Session 状态机、配置、日志、崩溃恢复（前端共享）
├── OwHelper/          # 控制台前端
├── OwHelper.Tray/     # 托盘前端（WinForms，暖纸主题：暖纸底/墨黑/焦赭强调）
└── BgKeyProbe/        # 诊断工具（消息矩阵实验、窗口清单、队列探测）
tests/
├── OwHelper.Core.Tests/          # 单元 + 假窗口集成测试 + 架构红线测试
├── OwHelper.App.Tests/           # Session 生命周期/配置/日志/托盘映射测试 + 实机替身集成测试
├── OwHelper.TestSupport/         # 测试基础设施（FakeWindow / 替身进程）
└── OwHelper.TestSupport.StandIn/ # 替身窗口程序（实机集成测试用）
```

架构规则（由测试强制）：
- Apps → Core 单向依赖；所有 P/Invoke 只在 Core；app 程序集中不得出现 `DllImport`；
- Core 不引用 WinForms/WPF；
- `src/` 源码中不得出现 `WriteProcessMemory` / `ReadProcessMemory` / `VirtualAllocEx` / `CreateRemoteThread` / `SetWindowsHookEx` 等红线 API。

## 构建与测试

```powershell
dotnet restore
dotnet build ow_helper.sln -c Release
dotnet test ow_helper.sln -c Release
```

测试包含**实机集成测试**：会启动一个自有窗口的替身进程（`OwHelperStandIn.exe`），验证自动重连、资源策略重应用、窗口移出/恢复、优先级往返等；不会触碰真实的 Overwatch（除手工验收外）。

发布：

```powershell
scripts\publish.ps1   # 输出 artifacts/OwHelper-win-x64（framework-dependent）
```

CI：GitHub Actions（windows-latest）：restore → build → test。

## 运行

### 托盘版（推荐）

```powershell
src\OwHelper.Tray\bin\Release\net8.0-windows\OwHelper.Tray.exe
```

托盘图标颜色即状态：苔绿=运行中、赭黄=等待/重连/资源部分失败、砖红=故障、暖灰=停止。
双击图标打开状态窗口；右键菜单：

```
开始 / 停止
移出屏幕 / 还原
重新检测 / 重挂
打开状态窗口
打开日志文件夹
打开配置文件
退出（恢复资源与窗口）
```

通知气泡：重连成功（PID 旧→新）、资源策略部分失败、连续脉冲失败、目标失联。

### 控制台版

```powershell
src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe

# 命令行参数覆盖配置文件的按键与间隔：
src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe shift,w 30
```

可用按键名：`shift` `ctrl` `alt` `lshift` `rshift` `lctrl` `rctrl` `lalt` `ralt` `space` `enter` `tab` `esc`、
`a`–`z`、`0`–`9`、`f1`–`f12`、`up` `down` `left` `right`、`insert` `delete` `home` `end` `pageup` `pagedown`、
`printscreen` `numlock` `numdivide`（方向键等扩展键会正确设置 extended-key 位），
以及**鼠标按钮**：`mouseleft`(`lmb`) `mouseright`(`rmb`) `mousemiddle`(`mmb`) `mouse4` `mouse5`
（后台合成点击已实机验证；点击位置固定在窗口客户区中心）。

## 设置界面（托盘）

右键托盘图标 → **设置…**（或状态窗口的「设置」按钮）：

- **按键**：OW 常用键位预设多选（LMB/RMB/MMB/侧键/Shift/E/Q/V/F/R/Space/Ctrl/WASD/1/2/Tab）+ 自定义输入（支持上表全部键名）
- **时序**：间隔 / 按住 / focus 等待 / 抖动
- **资源**：CPU 优先级、EcoQoS
- **窗口与行为**：移出屏幕、重连后保持移出、前台跳过
- **日志**：级别与保留天数

保存后：`config.json` 自动更新；间隔 / 抖动 / 优先级 / 前台跳过 / 窗口选项**立即生效**（运行中会重新应用资源策略并显示结果）；
**按键与按住、focus 时序在下次「开始」生效**（界面已注明）。

按键：

| 键 | 功能 |
|---|---|
| `空格` | 开始 / 停止挂机 |
| `m` | OW 窗口移出屏幕 / 还原 |
| `r` | 重新检测/重挂 OW（运行中会先恢复旧窗口位置与资源，再重挂） |
| `S` | 状态屏（State / Target / Alive / 间隔 / 脉冲数 / 日志路径 / GPU） |
| `L` | 显示最近 20 条日志 |
| `+` / `-` | 间隔 ±5 秒（5–300） |
| `q` | 退出（自动恢复优先级与窗口位置） |

## 配置文件

路径：`%LOCALAPPDATA%\OwHelper\config.json`（首次运行自动生成；启动时读取，不支持热重载）

```json
{
  "target": { "processName": "Overwatch" },
  "input": {
    "keys": ["shift"],
    "intervalSeconds": 30,
    "holdMilliseconds": 200,
    "focusWaitMilliseconds": 50,
    "sendFocus": true,
    "sendActivate": false,
    "sendActivateApp": false,
    "skipWhenTargetForeground": true,
    "jitterPercent": 0
  },
  "resource": {
    "priority": "BelowNormal",
    "ecoQoS": true,
    "gpuBackgroundFpsTarget": 20,
    "gpuPolicyMode": "GuideOnly"
  },
  "window": {
    "allowMoveOffscreen": true,
    "keepOffscreenAcrossRestart": false
  },
  "logging": { "level": "Information", "retainDays": 7 }
}
```

容错行为：文件缺失→生成默认值；JSON 语法错误→**不覆盖原文件**、本次用默认值并提示；未知字段忽略；
超范围数值自动收敛并打印原因；未知按键回退为 `shift` 并提示。命令行参数优先于配置文件。

## 日志与状态

- 日志：`%LOCALAPPDATA%\OwHelper\logs\owhelper-YYYYMMDD.log`（结构化 `时间|级别|事件|字段…`；保留 `retainDays` 天）
- 事件示例：`APP_START` `TARGET_FOUND` `TARGET_LOST` `TARGET_REATTACHED` `SESSION_START` `SESSION_STOP`
  `PULSE_OK` `PULSE_PARTIAL_FAILURE` `PULSE_SKIPPED_FOREGROUND` `RESOURCE_APPLY` `RESOURCE_APPLY_PARTIAL`
  `RESOURCE_RESTORE` `WINDOW_MOVE_OFFSCREEN` `WINDOW_RESTORE` `NATIVE_ERROR` `CONFIG_LOAD_FAILED`
- 崩溃恢复：窗口移出屏幕成功后写入 `%LOCALAPPDATA%\OwHelper\runtime-state.json`；下次启动会校验
  PID/启动时间/HWND 归属，一致时提示"按 Y 恢复"（绝不未经确认移动窗口）；失效状态自动清理。

## 已验证行为

- OW 在后台（非前台）时，脉冲可以触达游戏并生效；
- 脉冲不抢占真实前台焦点，浏览器/IDE 键盘输入不受影响；
- OW 是真实前台窗口时自动跳过本轮；
- OW 重启后自动重连并对新进程重新应用资源策略；
- 运行中 `r` 重挂不会丢失窗口位置快照；
- 停止/退出后恢复**原始**优先级（而非硬编码 Normal）与窗口位置；
- 单实例运行；崩溃残留的屏幕外窗口有恢复路径。

## Known limitations

- **最小化**状态下的脉冲效果尚未实机验证（已验证的是"后台可见"与"移出屏幕"；建议优先用 `m`）；
- EcoQoS 依赖系统电源方案：部分自定义电源方案下 `SetProcessInformation` 会失败，工具会如实报告（CPU 优先级不受影响）；
- GPU 后台限帧需要手动在 NVIDIA 驱动中配置（见下）；程序只检测与引导，不修改驱动配置；
- OW 是真实前台窗口时默认跳过脉冲（可用 `skipWhenTargetForeground=false` 关闭，但不建议）。

## 资源策略

| 项目 | 手段 | 状态 |
|---|---|---|
| CPU 调度 | 配置的优先级（默认 `BelowNormal`） | 开始时应用，停止时恢复原值 |
| 能效 | EcoQoS（Power Throttling） | 同上；不支持的系统会如实报告失败 |
| GPU | NVIDIA 驱动后台限帧（手动） | 见下 |

### NVIDIA 后台限帧（一次性设置）

NVIDIA App / NVIDIA Control Panel → Graphics / Manage 3D Settings → Program Settings → 选择 `Overwatch.exe` → **Background Application Max Frame Rate** → 设为 `20 FPS`。

## Troubleshooting

- **脉冲报错 `err=5`（拒绝访问）**：OW 与工具完整性级别不一致（如 OW 以管理员运行）。先尝试以相同权限运行；原则是最小权限，不建议长期管理员运行。
- **找不到 OW**：确认游戏进程名为 `Overwatch.exe`，然后按 `r`。
- **窗口移出屏幕后找不回来**：重启 OwHelper 会提示恢复；或选中任务栏窗口后按 `m` / `Win + 方向键`。
- **提示"OwHelper 已在运行"**：单实例限制；先退出已运行实例。
- **诊断**：`BgKeyProbe.exe` 可列出目标窗口、探测消息队列响应、执行单次脉冲实验（命令见其启动提示）。

## 隐私

本地运行，无网络请求，不上传任何数据。

## 安全边界与平台条款

本工具不做（也永不做）：anti-ban、undetected mode、stealth、进程隐藏、内存注入、DLL 注入、内核驱动、游戏函数 Hook、输入驱动伪造、反作弊绕过。

请自行阅读并遵守：

- [Blizzard EULA](https://www.blizzard.com/en-us/legal/08b946df-660a-40e4-a072-1fbde65173b1/blizzard-end-user-license-agreement)
- [Blizzard Anti-Cheating Agreement](https://www.blizzard.com/en-us/legal/cd5930c0-2784-420c-a23d-1e0d6ff8599b/anti-cheating-agreement)

## License

尚未选定，见 [LICENSE-TODO.md](LICENSE-TODO.md)。
