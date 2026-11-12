# PSAI 接口说明

本页面向通过 CLI 或 MCP 游玩的 agent。先读取当前状态与 guidance，再依照当下目标操作。

`progression` 提供原版开放标记；`guidance.availability` 区分 available、locked、temporarily_unavailable、not_observed 和 unknown。只有 `guidance.rules` 是当前适用的建议；`deferred_rules` 返回暂缓规则及原因，不要求现在完成。`status` 没有当前快照，先给基础指导，详细适用性在 `observe` 中计算，不复用前次存档状态。未知与暂时不可用都不代表存档损坏。开放状态随原版事件和当前场景读取，不用固定天数代替判断。普通电话入口和王尔德加点开放分别判断，联系人冷却仍按原版电话提示处理。

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

`trade` 提供当前意图、需求文字、预算、可见自有物品、出售候选、已备货物品、原版报价行与相关窗口文字。候选来自当前已知库存及其普通自有储物容器的实际物品实例，包含关闭的容器；对白中的说明物不等于持有物品。

出售备货位置是 `trade.offer_inventory`（`back_counter`）。`counter` 为门外前柜台（顾客放货处），`back_counter` 为店内交易备货台。两者都可能存在自有或未拥有物品，关门不会自动收回。原版出门会清除 `counter` 上的物品，包括自有物品；选包前应先将台面背包收回店内。优先在打烊前整理，窗口隐藏或未列出不代表物品已收回。读取当前库存条目的 `owned` 或查询 `tooltip`，用 `move_item` 移至 `inventory` 或已打开的合适容器，随后观察确认。当前交易中的报价、加工或提交物品在结束后收尾，避免误移顾客物品。

`trade.rows` 提供原版选择动作和选中状态。名称重复且无法唯一关联时，`matching_item_ids` 返回候选，`item_id` 留空。以物易物读取 `trade.barter` 的条件、候选和报价组；单件符合某一条件不代表整笔交换满足要求，最终核对 `conditions_met` 与原版按钮。

`customer_interaction` 区分事件对白、普通对白、对白选择和买卖，返回当前顾客关联、`primary_action_ids` 与 `secondary_action_ids`。动作的 `channel` 区分 dialogue、trade 和 customer_management，`recommended` 标明当前优先级；原版允许的次要操作仍保留，并非强制禁止。事件期间优先完成原版 choice；送客可能中断事件，不能替代事件本身的拒绝选项。确认框仍优先于顾客对白。`trade.interaction_context=dialogue_only` 表示此次为对白来访，不应将空报价当作普通交易故障。

## 库存、容器与加工

`inventory_catalog.items` 提供只读自有库存总览，包括关闭的普通储物容器。`catalog_id` 是会话内只读引用；`item_id` 只有物品当前可见并注册为交互目标时才提供，`visible` 和 `can_move_now` 分别描述当前是否可见及是否允许取出。`location_path` / `container_path` 表示位置和父容器，`requires_open` / `next_open_target` 指明是否及先打开哪里。执行 `open_panel` 后重新观察，嵌套容器逐层处理，取得当前 `item_id` 和可见库存 ID 再执行移动。目录读取不会自动打开窗口，也不将只读条目注册为操作目标；`inspect contents` 仍遵循窗口可见性。

`trade.owned_items` 和 `sell_candidates` 覆盖该目录；关闭容器里的候选 `id` / `item_id` 为 null，使用 `catalog_id` 识别和查位置。`visible_owned_items` 继续只含当前可见物品。以物易物条件同时提供 `matching_catalog_ids` 和兼容的 `matching_visible_item_ids`，候选仍需完整原版条件与当前报价确认。

不展开未购买的容器、关闭的机器槽位或尚未正常打开的追踪型包装。总览从当前已识别的库存出发，不扫描全部资源目录；未进入该范围的物品不能据此认定不存在。

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

`afterhour_ground` 是地上的物品；带回物品要放入口袋或背包。外出背包可长期保持打开。选包比较 `capacity.total_cells` 的绝对总容量。选包提示不等于携带成功：抵达并等待 `busy=false` 后，核对 `afterhour_backpack.items` 中的实际背包及对应 `afterhour_backpack_contents`。回店后背包可能先落在 `back_counter`，检查实际落点及包内物品后再整理。

## 提示、设置与教程

观察和动作快照中的 `attention` 同时在 CLI/MCP 回执顶层返回，延迟通过 `result` 查询也保留。`has_pending_response` 表示存在需要回应的确认或设置窗口；`items` 提供 `screen_id`、完整原版文字、`kind`、`requires_response`、`blocks_gameplay` 及当前 `controls`。对应 `screens` 保留详细窗口和设置值。

`guidance.focus` 将需回应窗口优先列出；只有教程说明时推荐先阅读再按原版任务推进。`blocks_gameplay=null` 表示尚未确认是否阻塞，不把普通提示当作卡死。`controls.enabled=false` 或游戏转场忙碌时先等待，再观察。

- 通用确认框使用 `confirm_prompt` / `cancel_prompt` 或原版按钮动作，先读内容再选择。单按钮也不表示可以无条件确认。
- 展示柜调整生效时间提示标记为 `topic=display_case_timing`，读取原版说明后确认；完整内容直接来自当前提示文本源，避免新弹窗首帧文字尚未渲染而漏读。
- 对白设置为 `dialogue_settings`，使用 `close_dialogue_settings` 或当前原版关闭按钮。游戏设置为 `game_settings`，使用 `close_game_settings`。设置值提供读取，关闭无需修改值；保留原版关闭流程。
- `tutorial_*` 返回当前可见教程说明及原版按钮。没有关闭按钮的箭头、操作提示通常随着完成任务消失，不提供统一跳过教程动作。

## 报告、图像与安全动作

每日报纸在 `daily_news` 中读取，使用 `close_daily_news` 关闭。晨间报告、日结和日历报告分别读取对应屏幕与动作。

`image_targets` 列出当前可查询的局部图像。`inspect` 的 `detail: image` 首次返回 pending，再次查询获取 PNG。默认先读结构化信息与文字，只有不足时取图。鉴定图像仅裁切当前打开的鉴定窗口。

当前顾客的独立通缉字段是项目明确开放的信息，输出标记来源。举报需传入 `evidence`。射击动作需当前 `firearm.target`，实际结果及后续对白继续观察；所有这些行为仍服从用户当前指令。

## 指南与文档阅读

正常激活持有的指南后，先读 `observe.documents`，再用其中的 `id` 查询：

```json
{"target":"multipage_guide","detail":"document"}
```

机械手、燃料匣、肋明治等指南共用 `multipage_guide` 阅读器；普通书册使用 `book`，单页纸张使用 `paper`。查询只返回当前可见页，不查询所有书籍资源或提前展开隐藏页面。

`text` 包含当前页文字，`body_text` 排除按钮文字；`text_status=no_visible_body_text` 表示没有可提取的正文，不表示文档为空。`graphic_count` 提示页内图形数量，需要辨认图案时以 `image_target` 按需查询 `detail=image`。多页指南还提供 `page_number`、`page_count` 和原版 `page_text`。

按当前 `actions` 的可用状态执行 `multipage_previous` / `multipage_next`、`book_previous` / `book_next`，通过文档的 `action_ids` 找到关闭和其他原版按钮。翻页后重新观察或查询正文；关闭后 `inspect document` 拒绝读取，旧页图像上下文失效。书册目录中的原版按钮也通过屏幕动作返回，未提供统一跳过或一次读取全部页面。
