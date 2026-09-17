# 输入设置与鼠标键支持：设计（Stage G）

> 实现现状：**已实现**（鼠标 L/R/M 后台点击实测通过、设置界面与热应用上线）。现役说明以 README 为准。

> 状态：已与用户确认（鼠标五键 / 固定窗口中心坐标 / 热应用+按键下次生效 / 先做实验）
> 关联：`docs/ow_helper_PRD_v1.0.md` §9（输入引擎）、§15（配置）

## 目标

1. 托盘前端提供**设置界面**，不再要求手改 `config.json`；
2. 键位模型支持 OW 常用键（含鼠标左/右/中/侧键 X1/X2）。

## 非目标

- 鼠标点击坐标可配置（v1 固定客户区中心）
- 任意鼠标轨迹/连点宏；不改 Raw Input 相关行为
- 自动写入驱动配置（GPU 仍为引导）

## 关键前置实验（S1）

OW 的键盘输入接受性已实测；**鼠标按钮通常走 Raw Input，窗口消息可能被忽略**。因此先扩展 `BgKeyProbe`：

- `l` = 左键：SETFOCUS → WM_MOUSEMOVE(中心) → WM_LBUTTONDOWN(200ms) → WM_LBUTTONUP → KILLFOCUS
- `n` = 同上但**不发 MOUSEMOVE**（验证移动是否必需）
- `r` = 右键、`m` = 中键（同 l 序列）
- 判定：训练场中左键开火/右键动作生效 → 支持鼠标；不生效 → 鼠标支持在项目边界内不可行（SendInput 抢焦点、驱动级禁止），仅做键盘版设置界面并记录结论

## 键位模型（S2）

沿用官方鼠标虚拟键码，配置格式与现有 `keys: ["shift"]` 完全兼容：

| 名称 | VK | 消息 | wParam |
|---|---|---|---|
| `mouseleft` | 0x01 | `WM_LBUTTONDOWN/UP` (0x0201/02) | `MK_LBUTTON` |
| `mouseright` | 0x02 | `WM_RBUTTONDOWN/UP` (0x0204/05) | `MK_RBUTTON` |
| `mousemiddle` | 0x04 | `WM_MBUTTONDOWN/UP` (0x0207/08) | `MK_MBUTTON` |
| `mouse4` | 0x05 | `WM_XBUTTONDOWN/UP` (0x020B/0C) | 高字 `XBUTTON1` |
| `mouse5` | 0x06 | 同上 | 高字 `XBUTTON2` |

- `lParam = (y << 16) | (x & 0xFFFF)`，坐标为**客户区中心**（`GetClientRect`）
- `PulseRunner` 按 VK 分流：键盘 → 现有 `WM_KEYDOWN/UP` 路径不变；鼠标 → MOUSEMOVE（可选）→ 按钮 DOWN/UP
- 别名：`lmb`/`rmb`/`mmb`

## 设置界面（S3）

`SettingsForm`（托盘，暖纸色板）+ `SettingsMapper`（纯函数：表单值 ⇄ `AppConfig`，可单测）：

| 区块 | 控件 |
|---|---|
| 按键 | CheckedListBox：OW 预设（LMB/RMB/Shift/E/Q/Space/Ctrl/W/A/S/D/1/2/V/F/R/Tab）+ 自定义输入（KeyNames 全量，含鼠标五键与方向键） |
| 时序 | 间隔 5–300s · 按住 10–2000ms · focus 等待 0–1000ms · 抖动 0–50% |
| 资源 | 优先级下拉（Normal/BelowNormal/AboveNormal/Idle/High）· EcoQoS 开关 |
| 窗口 | 允许移出屏幕 · 重连后保持移出 |
| 行为 | OW 前台时跳过 |
| 日志 | 级别（Debug/Information/Warning/Error）· 保留天数 1–365 |

保存流程：`AppConfig.Validate` → 写 `config.json` → 热应用：
- 立即生效：`IntervalSec` `JitterPercent` `Policy`（运行中会重新 Apply 并显示结果） `SkipWhenForeground` `AllowMoveOffscreen` `KeepOffscreenAcrossRestart`
- **下次“开始”生效**：keys / hold / focusWait（界面明示）

`Session` 新增 `UpdateRecipe(PulseRecipe)`；托盘菜单新增“设置…”。

## 测试

- Core：鼠标消息序列（消息类型、lParam 坐标、wParam 标志、X 键高字）、`IsMouseVirtualKey`、`KeyNames` 新名称
- App：`SettingsMapper` 往返（表单 ↔ 配置）、校验提示
- 实机：探针实验；设置界面改键 → 开始 → OW 内生效；LMB 后台开火
