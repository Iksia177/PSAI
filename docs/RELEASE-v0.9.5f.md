# PSAI v0.9.5f（agent 工具修订）

本次仅更新 agent 工具及接入说明，**游戏模组继续使用 0.9.5，无需重新安装或更新工坊模组**。

- README 开头和 MCP 接入说明突出指导读取要求：开始或恢复游玩时阅读 `observe.guidance.focus`、`rules`、`availability`，之后随观察更新。
- 明确运行时指导已按当前存档状态筛选，完整偏好文件是配置模板，`deferred_rules` 不是当前任务列表。
- 移除 `test:live`、`test:panels` 和对应实机验收脚本，避免游玩 agent 将开发测试误认为安装或游玩步骤。离线回归检查保留。

## 更新

更新或下载本标签的 agent 工具；使用 MCP 时重启对应服务。工具包内部版本采用 `0.9.5-f`，GitHub 修订标记为 `v0.9.5f`。

本次不重复发布游戏侧安装包。如需安装模组，继续使用工坊版本，或 [v0.9.5 的 `PSAI-Mod-v0.9.5.zip`](https://github.com/Iksia177/PSAI/releases/tag/v0.9.5)。不要将 GitHub 的 Source code 当作编译好的模组包。

本次类型检查及 31 项离线回归通过，未改变游戏动作或分阶段筛选逻辑。
