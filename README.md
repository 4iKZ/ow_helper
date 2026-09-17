# OW Helper

Windows 后台输入调度器 + 进程资源控制器：让 Overwatch 2 在**失去焦点/后台**时仍能收到定向按键脉冲，同时用户可以继续正常使用电脑；提供 CPU 优先级 / EcoQoS 节流与窗口移出屏幕能力，并在停止时恢复原状。

> 本工具只使用常规 Windows API（窗口消息投递 + 进程调度接口）。
> **不包含**任何反作弊绕过、DLL 注入、内存读写、API Hook、内核驱动、进程隐藏或检测规避能力。详见 [安全边界](#安全边界与平台条款)。

## 支持环境

- Windows 11 x64（Windows 10 未验证）
- .NET 8 SDK（构建）/ .NET 8 Desktop Runtime（运行）
- Overwatch 2，建议**窗口模式**

## 仓库结构

```text
src/
├── OwHelper.Core/     # 类库：Win32 互操作、窗口发现/身份校验、脉冲引擎、资源治理、窗口放置
├── OwHelper/          # 产品 CLI
└── BgKeyProbe/        # 诊断工具（消息矩阵实验、窗口清单、队列探测）
tests/
└── OwHelper.Core.Tests/
```

架构规则：Apps → Core 单向依赖；所有 P/Invoke 只在 Core；app 程序集中不得出现 `DllImport`（有测试断言）。

## 构建与测试

```powershell
dotnet restore
dotnet build ow_helper.sln -c Release
dotnet test ow_helper.sln -c Release
```

## 运行

```powershell
# 默认：Shift 键，30 秒间隔
src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe

# 自定义：多键 + 间隔（秒）
src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe shift,w 30
```

可用按键名：`shift` `ctrl` `alt` `lshift` `rshift` `lctrl` `rctrl` `lalt` `ralt` `space` `enter` `tab` `esc`、
`a`–`z`、`0`–`9`、`f1`–`f12`、`up` `down` `left` `right`、`insert` `delete` `home` `end` `pageup` `pagedown`、
`printscreen` `numlock` `numdivide`（方向键等扩展键会正确设置 extended-key 位）。

按键：

| 键 | 功能 |
|---|---|
| `空格` | 开始 / 停止挂机 |
| `m` | OW 窗口移出屏幕 / 还原 |
| `r` | 重新检测 OW（重启过游戏时用） |
| `+` / `-` | 间隔 ±5 秒（范围 5–300） |
| `q` | 退出（自动恢复优先级与窗口位置） |

## 已验证行为

- OW 在后台（非前台）时，脉冲可以触达游戏并生效；
- 脉冲不抢占真实前台焦点，浏览器/IDE 键盘输入不受影响；
- OW 是真实前台窗口时自动跳过本轮；
- 停止/退出后：CPU 优先级与窗口位置被恢复。

## Known limitations（v1.0 开发中）

- **最小化**状态下的脉冲效果尚未实机验证（已验证的是"后台可见"与"移出屏幕"；建议优先用 `m`）；
- EcoQoS 依赖系统电源方案：部分自定义电源方案下 `SetProcessInformation` 会失败，工具会如实报告（CPU 优先级不受影响）；
- GPU 后台限帧需要手动在 NVIDIA 驱动中配置（见下）；
- 无托盘 UI / 配置文件（规划中）；
- OW 是真实前台窗口时跳过脉冲（设计行为，防止误触技能）。

## 资源策略

| 项目 | 手段 | 状态 |
|---|---|---|
| CPU 调度 | `BelowNormal` 优先级 | 开始挂机时应用，停止时恢复 |
| 能效 | EcoQoS（Power Throttling） | 同上 |
| GPU | NVIDIA 驱动后台限帧（手动） | 见下 |

### NVIDIA 后台限帧（一次性设置）

NVIDIA App / NVIDIA Control Panel → Graphics / Manage 3D Settings → Program Settings → 选择 `Overwatch.exe` → **Background Application Max Frame Rate** → 设为 `20 FPS`。

## Troubleshooting

- **脉冲报错 `err=5`（拒绝访问）**：OW 与工具完整性级别不一致（如 OW 以管理员运行）。先尝试以相同权限运行；原则是最小权限，不建议长期管理员运行。
- **找不到 OW**：确认游戏进程名为 `Overwatch.exe`，然后按 `r`。
- **窗口移出屏幕后找不回来**：选中任务栏窗口后用 `Win + 方向键` 移回，或按 `m`。
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
