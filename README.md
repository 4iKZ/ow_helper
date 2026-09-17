# OW 助手

让《守望先锋》在**后台自动按键**的小工具：挂机时你可以正常用电脑，想自己玩时点一下停止。

> 只用常规 Windows 窗口消息，**不注入游戏、不读写游戏内存、不做反作弊绕过**。详见[安全边界](#安全边界与平台条款)。

## 快速开始（四步）

1. **打开《守望先锋》**，进入你想挂的房间或训练场
2. 双击 `OwHelper.Desktop.exe`（会打开一个大窗口）
3. 点**「托比昂战令一键启动」**（默认：每 30 秒自动按一次 Shift）
4. 正常用电脑就行；想自己玩时，再点一次同一个按钮停止

## 主窗口上都有什么

| 区域 | 说明 |
|---|---|
| 状态卡 | ● 已连接《守望先锋》· 正在挂机 · 已自动按键次数 · 下次按键倒计时 |
| 大按钮 | **托比昂战令一键启动** / 运行中变成**停止挂机** |
| 隐藏游戏窗口 | 把游戏窗口移到屏幕外，眼不见心不烦；再点一次就显示回来 |
| 怎么用（四步） | 常驻的使用说明，不用记 |
| 更多设置… | 换按键（含鼠标左/右/中/侧键）、调按键节奏、后台省电模式等 |

## 关掉窗口会怎样？

**挂机不会中断**：程序会最小化到右下角托盘（首次会弹一个小提示）。想回来时**双击托盘图标**；想停止或退出，**右键托盘图标**即可。

托盘图标颜色就是状态：绿色=挂机中、黄色=等待/重连、红色=故障、灰色=已停止。

## 更多设置

- **自动按哪些键**：勾选 OW 常用键位（含鼠标左键/右键/中键/侧键、Shift、E、Q、Space、Ctrl、WASD 等）；也可以自己填英文键名
- **按键节奏**：每隔多少秒按一次、每次按住多久、间隔是否小幅随机
- **后台运行方式**：CPU 优先级别、后台省电模式（部分电脑不支持，会自动跳过并提示，不影响挂机）
- **其他**：玩游戏时自动暂停、允许隐藏游戏窗口、游戏重开后保持隐藏
- 保存后：节奏与后台选项**立即生效**；按键的变化在**下次点「开始」时**生效

## 常见问题

| 现象 | 处理 |
|---|---|
| 状态卡显示"还没有找到《守望先锋》" | 先打开游戏；或点右键托盘 → 重新连接游戏 |
| 提示"后台省电模式没有生效" | 你的电源方案不支持，可忽略，不影响挂机 |
| 提示"按键发送失败了几次，可能是权限问题" | 用**管理员身份**双击 `OwHelper.Desktop.exe`（或把游戏改为普通权限运行） |
| 游戏窗口找不到了 | 主窗口点「显示游戏窗口」；或选中任务栏窗口按 `Win + 方向键` |
| 提示"OW 助手已在运行" | 本程序只能开一个：双击托盘图标打开已在运行的那个 |

## 给折腾党（技术细节）

- **控制台版**（无窗口/脚本场景）：`src\OwHelper\bin\Release\net8.0-windows\OwHelper.exe [按键,逗号分隔] [间隔秒]`
- **诊断工具**：`BgKeyProbe.exe`（窗口清单、消息矩阵实验、单次脉冲测试）
- **配置文件**：`%LOCALAPPDATA%\OwHelper\config.json`（首次运行自动生成，界面保存时改写）
- **运行记录**：`%LOCALAPPDATA%\OwHelper\logs\owhelper-YYYYMMDD.log`（结构化事件，右键托盘 → 打开运行记录）
- **异常恢复**：窗口隐藏时异常退出会写 `runtime-state.json`，下次启动询问是否恢复
- **构建与测试**：`dotnet build ow_helper.sln -c Release` / `dotnet test ow_helper.sln -c Release`
  （测试包含使用替身窗口进程的实机集成测试与架构红线检查：无 P/Invoke 泄漏、无注入类 API）
- **发布**：`scripts\publish.ps1` → `artifacts\OwHelper-win-x64`
- **目录结构**：`src/OwHelper.Core`（Win32/输入/资源）、`src/OwHelper.App`（会话/配置/日志）、
  `src/OwHelper.Desktop`（桌面界面）、`src/OwHelper`（控制台）、`src/BgKeyProbe`（诊断）

## 支持环境

Windows 11 x64 · .NET 8 Desktop Runtime · Overwatch 2（建议窗口模式）

## 安全边界与平台条款

本工具不做（也永不做）：anti-ban、undetected mode、stealth、进程隐藏、内存注入、DLL 注入、内核驱动、游戏函数 Hook、输入驱动伪造、反作弊绕过。

请自行阅读并遵守：

- [Blizzard EULA](https://www.blizzard.com/en-us/legal/08b946df-660a-40e4-a072-1fbde65173b1/blizzard-end-user-license-agreement)
- [Blizzard Anti-Cheating Agreement](https://www.blizzard.com/en-us/legal/cd5930c0-2784-420c-a23d-1e0d6ff8599b/anti-cheating-agreement)

## License

尚未选定，见 [LICENSE-TODO.md](LICENSE-TODO.md)。
