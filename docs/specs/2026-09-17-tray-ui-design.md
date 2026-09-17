# OwHelper.Tray 托盘 UI 设计（Stage E）

> 实现现状：**已被取代**——托盘已改为「大主窗口 + 托盘常驻」，文中的 StatusForm 由 MainForm 取代（见 Stage H 设计与 README）。现役说明以 README 为准。
> 状态：已与用户确认（配色 C / 共用互斥体二选一 / 完整状态窗口）
> 关联：`docs/ow_helper_PRD_v1.0.md` §19、§20、§34

## 目标

为 OwHelper 提供常驻托盘前端：状态可见、一键起停、窗口移出/还原、日志与配置入口。复用现有 `Session`（不复制任何逻辑）。

## 非目标

- 设置图形界面（以"打开 config.json"代替）
- 开机自启、通知中心、皮肤系统、多语言
- 替代控制台前端（CLI 保留，两者共用单实例互斥体，二选一运行）

## 架构

```text
src/
├── OwHelper.Core/     # 不变（Win32、目标、脉冲、资源、窗口、GPU）
├── OwHelper.App/      # 新类库（E1 抽取）：Session、SessionState、AppConfig、AppLog、
│                      #   RuntimeStateStore、RuntimeRecovery、SingleInstance
├── OwHelper/          # 控制台前端：Program + ConsoleUi（引用 App）
└── OwHelper.Tray/     # 托盘前端：TrayApplicationContext、StatusForm、Palette、TrayIconFactory（引用 App）
```

规则：托盘不得出现任何 P/Invoke（沿用架构测试红线）；Core 仍不引用任何前端。

## 交互

托盘菜单：

```
OW Helper — 运行中 · PID 18324 · 12 秒前脉冲     ← 状态摘要（禁用项，随状态着色）
────────────
开始 / 停止
移出屏幕 / 还原
打开状态窗口
打开日志文件夹
打开配置文件
────────────
退出（恢复资源与窗口）
```

- 状态灯（图标颜色）：运行=苔绿、等待/重挂/资源部分失败=赭黄、故障=砖红、停止=暖灰
- 气泡通知：重连成功（PID 旧→新）、资源部分失败、连续脉冲失败
- 状态窗口：等价控制台 `S` 屏（State / Target / Alive / Interval / Pulses / Log / GPU）+ 起停/移窗/打开日志四个按钮，1 秒刷新
- 闭环：退出时执行 `Session.CleanupAsync()`（恢复优先级、还原窗口、清理 runtime-state）

## 视觉系统（暖纸 · 墨与印）

| 用途 | 色值 |
|---|---|
| 窗口底 | `#F4F1EA` |
| 面板 | `#FBF9F4` |
| 描边/分隔 | `#D8D2C4` |
| 主文本 | `#2B2724` |
| 次要文本 | `#6E675E` |
| 强调（选中/按钮/主线） | `#C2542B`，悬浮 `#A8461F`，选中底纹 `#E8CFC2` |
| 状态·运行 | `#4F7A3A` |
| 状态·等待/重挂/部分失败 | `#B07A1E` |
| 状态·故障 | `#A63A2E` |
| 状态·停止 | `#8C857A` |

字体：Segoe UI（中文自动回退雅黑），正文 9pt、状态标签 12pt。

图标：`System.Drawing` 运行时绘制（16/32px），**双描边脉冲折线**——外层 2.5px `#F4F1EA` 纸色轮廓 + 内层 1.5px 状态色折线。理由：任务栏明暗主题不确定时，纸色轮廓保证状态色可读；不提交二进制资源。

菜单渲染：`ToolStripProfessionalRenderer` 子类套用色板（底色、选中底纹、文本色），不使用系统默认蓝色高亮。

## 错误与状态

- 所有命令经 `TrayController` 串行调用 `Session` 的 async API（不阻塞 UI 线程）；异常写入 AppLog 并以气泡提示
- 状态映射集中在 `TrayStatusMapper`（纯函数，可单测）：`SessionState` + 标志位 → 颜色/文案

## 测试策略

- 单测：`TrayStatusMapper`（状态→颜色/文案）、菜单启用规则
- 回归：E1 抽取后 108 项测试全绿；架构测试新增 `OwHelper.App`、`OwHelper.Tray` 无 P/Invoke
- 手工：图标/菜单/状态窗/气泡/退出恢复（可用替身进程 `OwHelperStandIn` 演练断链重连）
