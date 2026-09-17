# Stage G 实施计划（设置界面 + 鼠标键）

> 设计：`docs/specs/2026-09-17-input-settings-design.md`
> 规则：每个提交可构建、测试全绿、独立推送。

## S1 — 鼠标实验（探针 + Core 原语）

- `Native`：鼠标消息常量（`WM_MOUSEMOVE/LBUTTON*/RBUTTON*/MBUTTON*/XBUTTON*`、`MK_*`、`XBUTTON1/2`）、`GetClientRect`。
- 新增 `Core/MouseInput.cs`：`IsMouseVirtualKey` / `TryGetClientCenter` / `SendMove` / `SendButton`（lParam 中心坐标，wParam 标志，X 键高字）。
- `PulseRunner`：补 `SendFocus` / `SendKillFocus`（探针与后续鼠标脉冲复用）。
- `KeyNames`：`mouseleft/mouseright/mousemiddle/mouse4/mouse5` + `lmb/rmb/mmb`。
- 探针命令：`l`（含 MOUSEMOVE）、`n`（不含）、`r`（右键）、`m`（中键）。
- 测试：`MouseInputTests`（消息/坐标/wParam/X 键）、KeyNames 扩充。
- 验证：Debug/Release 构建 + 全量测试；随后请用户实机实验。

## S1b — 实机实验（需用户）

用户开 OW 训练场 → 探针输入 `l` / `n` / `r` / `m` → 观察开火/动作 → 结论写入本文档。

**结论（2026-09-17 实测）：通过。** LMB / RMB / MMB 在 OW 后台（终端前台）下均产生实际动作，
`n`（不发 MOUSEMOVE）与 `l` 表现一致；证明 OW 的鼠标按钮输入同样走窗口消息路径，
后台合成点击在项目边界内可行。后续 S2/S3 继续。

## S2 — 鼠标并入脉冲引擎

- `PulseRunner` 分流：`Keys` 中的鼠标 VK → `MOUSEMOVE + BUTTON DOWN/UP`（保持 SETFOCUS/KILLFOCUS 契约与消息结果记录）。
- 测试：配方含 `mouseleft` 的完整序列断言（含坐标）、混合键（键盘+鼠标）顺序与逆序。
- 如果 S1b 失败：本步骤取消，只保留键名解析报错提示“系统/游戏不支持鼠标键”。

## S3 — 设置界面

- `OwHelper.Tray/SettingsForm.cs`（暖纸色板）+ `SettingsMapper.cs`（纯函数）。
- `Session.UpdateRecipe`；保存 → 校验 → 写配置 → 热应用（Policy 运行中重新 Apply）。
- 托盘菜单“设置…”，状态窗加“设置”按钮。
- 测试：`SettingsMapperTests`（表单→配置、配置→表单、非法值回退）。
- 手工：改键（含鼠标）→ 开始 → OW 内生效；改间隔/优先级立即生效。

## S4 — 收尾

- README：键位表加鼠标、设置界面章节；spec/plan 状态同步。
- CI/publish 自动覆盖；架构红线保持（鼠标消息在 Core，托盘无 P/Invoke）。

---

## 执行状态（更新）

- **S1 完成**：Core 鼠标原语（`MouseInput`：中心坐标、`MK_*` 标志、X 键高字）+ 探针 `l/n/r/m`；测试覆盖消息/坐标/标志。
- **S1b 通过（用户实测）**：L/R/M 在 OW 后台均有实际动作 → 鼠标后台合成点击可行。
- **S2 完成**：`PulseRunner` 分流鼠标与键盘，混合键按下/抬起顺序正确；4 个新测试。
- **S3 完成**：`SettingsForm`（暖纸主题、OW 预设含鼠标五键、自定义输入）+ `SettingsMapper`（7 个测试）+ 热应用
  （`Session.UpdateRecipe` / `ReapplyPolicyAsync` / `TrayController.ApplySettingsAsync`）+ 托盘菜单与状态窗入口。
- 测试规模：104（Core）+ 57（App）= **161**。
- 待人工验收：设置界面交互（按键多选/自定义/保存提示）、运行中改间隔的即时生效、改键后「开始」用新键。
