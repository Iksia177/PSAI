# PSAI v0.9.4（预发布）

新增普通自有储物容器的只读库存总览，让 agent 查货时能看到关闭容器中的实际物品，同时保留正常打开、取货及交易流程。

- `inventory_catalog.items` 返回真实自有物品、数量、位置及嵌套容器路径，关闭窗口不会使正常储物内容从总览消失。
- 出售与以物易物候选覆盖该目录，标注 `requires_open`、`next_open_target` 和当前是否可移动；目录候选与原版实际报价分别返回。
- `catalog_id` 只读。打开必要容器并重新观察后，才取得可操作的 `item_id`，不能用目录引用直接移动物品。
- 保留 `inventories`、`inspect contents` 和 `visible_owned_items` 的可见性语义。未购买容器、关闭的机器内部、未正常打开的追踪型包装不被这次总览展开。

## 下载与安装

1. 下载 Assets 中的 `PSAI-Mod-v0.9.4.zip`，校验值见 `SHA256SUMS.txt`。Source code 是源码包。
2. 退出游戏，将 ZIP 内的 `psai` 文件夹放入游戏目录的 `Mods/`，确认有 `Mods/psai/PSAI.Mod.dll` 和 `Mods/psai/manifest.xml`。
3. 启用模组并重启游戏。Agent 侧同步更新本项目并重启 MCP；用户进入可操作存档后再检查连接。

安装编译模组无需 .NET SDK 或自行构建。工坊和 GitHub Release 分别发布，以实际安装版本为准。

版本统一为0.9.4，协议仍为0.1。离线检查与原版扫描通过；关闭容器候选、嵌套取货和实际成交还需实机样本复测，不保证全部容器或系统兼容。
