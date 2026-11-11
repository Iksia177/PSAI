# PSAI 接口说明

本页面向通过 CLI 或 MCP 游玩的 agent。先读取当前状态与 guidance，再依照当下目标操作。

## 观察、动作与结果

`status` 是接入检查，`observe` 返回当前快照，`actions` 列出动作，`inspect` 查询目标详情，`act` 执行动作，`result` 按命令 ID 查询回执。目标 ID 和动作 ID 来自最新观察，不自行猜测游戏资源 ID。

```powershell
node src/client/cli.ts inspect '{"target":"<item_id>","detail":"tooltip"}'
node src/client/cli.ts act '{"action":"begin_control"}' --revision <revision> --session <session_id>
node src/client/cli.ts result <command_id> --session <session_id>
```

动作必须携带最新会话和状态版本。`completed` 之后继续观察实际效果；`uncertain` 时先核对状态，再决定后续操作。对白的 busy 状态若同时提供可用推进动作，可按原版流程继续；转场和动画需等待。

## 顾客与交易

`customer_context` 包含会话内实例 ID、原版标识、显示名称、到达状态和通缉信息。实例 ID 与射击目标一致。`customer_queue.remaining` 不含当前顾客，使用原版等待队列计数。`dialogue.full_text` 返回当前对白完整文字，顾客关联随其更换更新。

`trade` 提供当前意图、需求文字、预算、可见自有物品、出售候选、已备货物品、原版报价行与相关窗口文字。候选来自当前可见的实际库存；对白中的说明物不等于持有物品。

出售备货位置是 `trade.offer_inventory`（`back_counter`）。`counter` 为顾客放货的前柜台，两个台面都可能存在自有或未拥有物品，关门不会自动收回。交易前整理遗留自有物品，再备货；交易结束后再次整理，避免误移顾客物品或尚在处理的报价。

`trade.rows` 提供原版选择动作和选中状态。名称重复且无法唯一关联时，`matching_item_ids` 返回候选，`item_id` 留空。以物易物读取 `trade.barter` 的条件、候选和报价组；单件符合某一条件不代表整笔交换满足要求，最终核对 `conditions_met` 与原版按钮。

## 库存、容器与加工

`inventories` 返回当前可见库存及中文名称，`panels` 提供容器窗口目标。使用 `open_panel` / `close_panel`，移动使用 `move_item`，其 `destination` 来自当前可见库存。嵌套容器需要保持必要父窗口可见，读取结束后逆序关闭。展示柜是具有经营作用的常驻区域。

详情查询包括：`description`（简短介绍）、`tooltip`（原版标签、效果和操作条件）、`contents`、`machine`、`capacity`（总格数）、`liquid`（原版品质及液量）、`operations`（可见作用目标）和 `use_conditions`（同时传入 `item`）。

`use_item_on` 使用 `item` 与 `target`，复用原版条件和处理流程，用于工具、加工及材料升级。机器按可见槽位、按钮、标签页、单选与滑块提供控件；参数以当前 `controls` 为准。物品移动和作用于目标是不同操作。

## 鉴定

正常使用放大镜作用于物品后，`observe.inspection` 返回当前鉴定界面：

- `id`、`item_id`、名称和窗口文字；`features.kind` 区分香烟 `cigarette`、机械手 `mechanical_hand`、肋明治 `ribwich`、燃料匣 `fuel_cell`。
- 香烟提供当前展示的封条、字样、标志与包装变体。机械手提供展示的材质、磨损、附加物与接口类型。肋明治提供肉饼、面包、包装、标志、可见生产日期和工厂编号。燃料匣提供可见序列号、图形部件及颜色。
- `variant_index` 只是当前显示图形的变体编号；`graphics.sprite` 是当前展示图形的资源名，`tint_rgba` 是叠加色。这些渲染标识不是库存物品 ID，不代表真假、质量等级或正确价值。结合已取得的原版说明资料判断；需要辨认图案时使用 `image_target`。
- `action_ids` 关联原版按钮，`controls` 包含窗口关闭、可见选项及估值输入。仅当前窗口显示并允许使用的控件可操作。

```powershell
node src/client/cli.ts inspect '{"target":"<inspection.id>","detail":"appraisal"}'
node src/client/cli.ts act '{"action":"set_appraisal_value","target":"<inspection.id>","value":100}' --revision <revision> --session <session_id>
node src/client/cli.ts act '{"action":"close_inspection","target":"<inspection.id>"}' --revision <revision> --session <session_id>
```

估值输入仅在原版显示可编辑输入框时可用。选项动作按返回参数传入 `target` 和 `value: 0` 或 `1`；提交估值、质疑或其他确认使用当前原版按钮。设置估值不等于提交。接口不返回隐藏的真假或正确估值，窗口关闭后鉴定详情与控件不再可用。

## 电话、加点与夜间外出

电话使用 `open_phone`、`dial_phone`（`number`）、`hang_up` 与 `close_phone`。读取 `contact_cards` 获取名片号码，查看后关闭；首次拨打后核对原版历史。通话对白和选项跟随原版流程。

加点读取可见项目、文字、前置与支付提示。专用币不足默认拒绝购买，明确决定允许现金补差时传 `allow_cash: true`。`save_context` 提供原版浏览间隔和按存档归属的购买记录。

`outing` 返回 `unlocked`、`blocked_reasons`、`can_depart_now`、剩余次数及原版外出日期。未解锁或条件不足时不要重复请求。通过 `open_outside_confirmation` 正常出门，`travel-*` 是当前已发现地点，`return_store` 正常返回。实际执行前再次校验原版条件。

`afterhour_ground` 是地上的物品；带回物品要放入口袋或背包。外出背包可长期保持打开。选包比较 `capacity.total_cells` 的绝对总容量。

## 报告、图像与安全动作

每日报纸在 `daily_news` 中读取，使用 `close_daily_news` 关闭。晨间报告、日结和日历报告分别读取对应屏幕与动作。

`image_targets` 列出当前可查询的局部图像。`inspect` 的 `detail: image` 首次返回 pending，再次查询获取 PNG。默认先读结构化信息与文字，只有不足时取图。鉴定图像仅裁切当前打开的鉴定窗口。

当前顾客的独立通缉字段是项目明确开放的信息，输出标记来源。举报需传入 `evidence`。射击动作需当前 `firearm.target`，实际结果及后续对白继续观察；所有这些行为仍服从用户当前指令。
