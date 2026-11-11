# PSAI

Probably Stolen Agent Interface，为 Steam 版《深空当铺：可能是偷的》提供 agent 游玩接口。

首个正式版本为 v0.1.0。基础营业、容器、日结、水培、电话、加点、材料升级、液体加工、中期交易、射击及后续对白、常驻背包和原版外出次数已有实机验证。支持原版游戏 0.46D、VERSION 100-Rev13，验收 Steam build 为 26525552。版本范围见 `docs/RELEASE-v0.1.0.md`，完整测试进度见 `docs/STATUS.md`。

## 已确认的方向

- 原生 C# 模组负责读取状态和执行正常游戏动作，外部 TypeScript CLI 与 MCP 适配器共用协议。
- 模组通过官方自有数据目录 API 交换 JSON，在 Unity 主线程处理游戏对象。
- 优先读取结构化接口与文字，图像按需查询。当前顾客的独立内部通缉身份字段已获用户授权读取，观察结果标记来源。
- 首版验收完成一个正常营业日，涵盖顾客、对话、库存、议价、交易与日结。

## 安装与首次使用

推荐安装方式为游戏侧订阅 Steam 创意工坊的 PSAI 模组，agent 侧下载本 GitHub 项目并使用 CLI / MCP 工具。PSAI 工坊条目尚待发布，实际订阅链接将在发布后补入。当前可按下文手动构建与安装游戏侧模组。

下载工具后，使用 Node.js 24 执行 `npm ci --ignore-scripts --registry=https://registry.npmjs.org` 即可完成工具准备。准备完成后告知用户下一步安装与载入要求，桥接检查安排在用户确认已进入可操作存档之后。

游戏侧完成安装后，由用户启用 PSAI、重启游戏并载入独立存档。用户确认就绪后，再执行 `node src/client/cli.ts status`，确认 online、ready、compatible 后读取 observe。缺少桥接、游戏离线或停在主菜单时，按 recovery 提示说明所需人工操作，保存进度并结束本轮，收到用户就绪确认后继续。

## 手动安装与本机开发

需要 PowerShell 7、global.json 指定的 .NET SDK 和已安装的 Steam 游戏。后续 CLI 与 MCP 使用 Node.js 24。

将 `psai.local.example.json` 复制成 `psai.local.json`，填写游戏安装路径。该本机配置被 Git 忽略。脚本也支持显式传入 `-GameDirectory`。

```powershell
Copy-Item psai.local.example.json psai.local.json
pwsh -NoProfile -File scripts/Inspect-Game.ps1
pwsh -NoProfile -File scripts/Build.ps1
pwsh -NoProfile -File scripts/Scan.ps1
```

脚本会在常见安装目录寻找 `dotnet.exe`，可处理 SDK 已安装但尚未加入 PATH 的情况。构建从本机游戏目录引用程序集，通过本地空包源还原，无需下载 NuGet 依赖。SDK 缓存位于 `.cache/`。

产物位于 `artifacts/psai/`，仅包含模组 DLL 和清单。扫描结果保存在 `artifacts/scan.json`，静态检查结果保存在 `artifacts/compatibility.json`。这些生成文件均被 Git 忽略。

退出游戏后运行 `pwsh -NoProfile -File scripts/Install.ps1` 安装到 `Mods/psai/`。安装器先扫描，再核对已有文件及安装回执，校验复制后的哈希。在模组菜单启用后重启并载入独立测试存档。

## CLI 与 MCP

Node.js 24 可以直接运行本项目的 TypeScript 入口。开发者可以离线检查类型和运行自动测试。

```powershell
npm ci --ignore-scripts --registry=https://registry.npmjs.org
npm run check
npm run build
npm test
```

以下命令在用户确认存档已载入后执行。

```powershell
node src/client/cli.ts status
node src/client/cli.ts observe
node src/client/cli.ts actions
```

CLI 默认使用当前用户的 `AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/psai`。可使用 `--data-dir` 或 `PSAI_DATA_DIR` 指定路径。`observe` 回执提供 `session_id` 和 `data.revision`，动作必须携带这两个值。

该目录是 PSAI 的正常通信目录。CLI 写入 intent、request 和临时发布文件，模组写入 status、result 及按存档归属的加点记录，并消费已处理请求。工具接入需要对该目录读写。游戏存档与全局设置不属于桥接故障的自动修复入口。未知 AppData 路径的权限请求需要保留具体路径与原文再核对。

```powershell
node src/client/cli.ts act '{"action":"begin_control"}' --revision 1 --session <session_id>
node src/client/cli.ts inspect '{"target":"item-1","detail":"description"}'
node src/client/cli.ts result <command_id> --session <session_id>
```

实际 revision 与对象 ID 来自最新观察。`begin_control` 临时切换到手动对话和手动叫号，`end_control` 恢复原选项。工具输出是结构化数据，游戏中的文本按游戏内容处理。

MCP stdio 启动命令为 `node D:/CodexWork/PSAI/src/client/mcp.ts`。在支持 MCP 的 harness 中将它配置为本地 stdio 服务，可选设置 `PSAI_DATA_DIR`。工具为 `psai_status`、`psai_observe`、`psai_inspect`、`psai_actions`、`psai_act`、`psai_result`。`psai_act` 明确要求观察中的 `session_id` 和 `expected_revision`。

`act` 返回 completed 表示正常操作处理函数已返回，随后应重新观察动画和后续面板。超时使用原命令 ID 查询，重试复用同一 ID 和完全相同参数。uncertain 表示需要重新观察后判断实际结果。

剧情事件的 busy 表示普通游戏操作受限。若 dialogue_state.can_interact=true 且动作清单仍有启用的对白推进或选项，可按原版流程继续，动画与转场期间仍需等待。

容器扩展包已实现 `panels` 观察字段、`open_panel` / `close_panel` 动作和 `contents` / `machine` 查询。固定容器存取、嵌套容器、交易期间自有容器读取、日结报告关闭，以及两台水培的槽位读取与真实夜间生产均已通过实机测试。`target` 取自最新观察的面板 ID，`move_item` 的 `destination` 取自已打开窗口中的库存 ID。固定展示柜为常驻区域，文档柜和垃圾桶的内部库存通过其物品窗口显示。机器面板按可见槽位返回库存及槽位标签，标签页仅读取当前选中页。鉴定细节与局部图像仍待扩展。

```powershell
node src/client/cli.ts inspect '{"target":"<panel_id>","detail":"contents"}'
node src/client/cli.ts act '{"action":"open_panel","target":"<panel_id>"}' --revision <revision> --session <session_id>
```

## 跨会话游玩偏好

CLI 和 MCP 的 `status`、`observe` 返回 `guidance`，包含偏好版本和按 priority 从小到大排列的建议。第 6 版增加推荐每天开店前、确认成交后、外出回店后向用户简短回报结果、选择理由及下一步意图，用户可调整频率。交易指导要求读取真实候选及原版报价。周期交租、产权目标、晨间机器整理、窗口收尾和其他建议继续提供。

## 顾客、交易与新闻

customer_context 提供会话内 id、原版 game_id、名称、到达状态和已授权的通缉信息，该 id 与 firearm.target 一致。customer_queue.remaining 是原版已排队但尚未到达的数量，不包含当前顾客，可能包含已排入队列的事件来访。dialogue 保留已显示文字，并在 full_text 返回当前对白完整文本，customer_id 关联已到达的当前顾客。

trade 返回当前顾客关联、原版意图、mode_text、需求文字、预算、visible_owned_items、sell_candidates、counter_items、sell_offer_items、rows 和相关窗口文字与 action_ids。sell_candidates 仅从当前可见库存的实际自有物品产生，说明查询目标不会加入候选。候选匹配需求仍需核对预算、类型、容量和原版动作条件。

出售物品应放入 `trade.offer_inventory`，其值为 `back_counter`，中文名称为交易备货台。`counter` 是前柜台。移动后重新观察原版 sell_offer_items 与平板 rows，再使用当前动作完成交易。rows 包含原版物品行的选择动作、文字和选中状态；名称重复时 matching_item_ids 返回多个可能实例，item_id 为空，避免猜测实例对应关系。每次操作使用最新会话和 revision，动作结果仍需复核。

每日报纸使用独立的 daily_news 屏幕，读取后执行 close_daily_news。它与晨间报告及日结报告分别读取、关闭。

性能测量使用 `node scripts/Measure-Bridge.ts` 保存离线目录枚举对比。用户确认存档已载入后可用 `node scripts/Measure-Bridge.ts --live` 测量只读请求。设置 `PSAI_PROFILE=1` 时 CLI/MCP 回执附带 client_performance，包含端到端耗时与回执体积。原版桥接回执的 performance 提供快照耗时、生成次数、目录枚举耗时、文件数和处理请求数，status 也提供目录统计。诊断数据不参与状态 revision。离线代理数据与 Unity 实测分别报告。

外出使用原版店门 OnClick 入口及其条件。outing.visits_remaining 返回原版当晚剩余次数，can_depart_now 与门的提示说明当前是否可以出发，实际调用前再次检查。原版的次数、伤势、封锁、休息及解锁条件生效，次数变化随原版状态读取。

默认偏好随接口代码分发。需要覆盖时，将示例复制为本机文件，并向 CLI 或 MCP 进程传入环境变量。覆盖文件提供完整规则列表，删除某条规则即可去掉相应偏好。

```powershell
Copy-Item psai.preferences.example.json psai.preferences.local.json
$env:PSAI_PREFERENCES_FILE = (Resolve-Path psai.preferences.local.json).Path
node src/client/cli.ts status
```

MCP harness 使用同名环境变量设置绝对路径。配置错误时返回默认偏好，并在 guidance.error 标记 preferences_invalid_or_unreadable。本机覆盖文件已被 Git 忽略。

## 中期交互

outing 同时返回原版 lastAfterhourOutingDay，与当前游戏日比较判断当晚是否已外出，重载后重新读取。出发是否可用以原版 visitLeftTonight 及店门条件为准。物品列表和 tooltip 的 owned 使用原版“未拥有”提示的同一判定，供 agent 收回交易台与柜台自有物品。

firearm 返回当前顾客的会话内 target、举枪状态、可用条件、射击登记和后续枪战状态。动作 raise_shotgun、lower_shotgun、shoot_customer 复用原版入口，shoot_customer 必须携带本次观察的 firearm.target，执行前核对目标与射击条件。completed 仅表示原版处理函数返回，实际击杀、存活和后续事件须继续观察。举枪与击杀专项等待测试副本实测。

`inspect operations` 查询当前物品对其他已可见物品的合法作用目标，配合 tooltip 与 use_conditions 自行发现加工、升级和工具用途。capacity 返回原版背包格形的 total_cells、width、height，总容量不含嵌套容器。liquid 返回原版当前条件下的液量与品质说明。机器面板除按钮、标签页与槽位外，增加单选控件和滑块，动作参数按 controls 中的 parameters 提供 value 或 low/high。滑块使用原版步进和变化、释放回调，实机样本待验证。

observe 增加 obligations，提供原版交租期限、金额、产权与按揭状态。customer_identity 提供已授权的独立 wanted、wanted_poster_id、stealth_wanted 和来源。通缉身份与普通犯罪线索分别读取。response_windows 提供原版响应窗口状态，screens 中的响应页列出选项、条件、tooltip_target 对应目标及正常动作，close_response 取消可关闭选择。背包选择尽可能关联原版候选背包及总容量。

contact_cards 仅在对应名片打开时返回号码及 close_action，号码来自该名片关联的原版联系数据。先读取、关闭名片，再正常拨号并核对历史。关闭后的名片与选择目标不再可查询。加点购买记录是已执行操作的日志，重载后结合当前存档状态复核。

`inspect` 增加 `tooltip`、`use_conditions` 和 `image`。tooltip 读取原版悬浮说明，包含当前条件下的标签与效果。use_conditions 需要 item 与 target，返回正常作用条件，执行动作是 use_item_on。可见选项的 tooltip_target 用于查询选项说明。展示柜客户效果通过 open_clientele 打开，读取屏幕或查询 display_case 的 tooltip，完成后 close_clientele。

```powershell
node src/client/cli.ts inspect '{"target":"<item_id>","detail":"tooltip"}'
node src/client/cli.ts inspect '{"item":"<source_id>","target":"<target_id>","detail":"use_conditions"}'
node src/client/cli.ts act '{"action":"use_item_on","item":"<source_id>","target":"<target_id>"}' --session <session_id> --revision <revision>
```

电话使用 open_phone、dial_phone、phone_advance、phone-choice-*、hang_up 和 close_phone。number 为 1～9 位数字字符串，通讯录、拨号状态与通话对白按可见内容读取。加点菜单通过正常电话打开，项目选择和购买动作来自当前 actions。购买动作的 payment 字段说明费用、专用币和默认支付可用性，默认专用币不足时拒绝，明确携带 allow_cash=true 可允许原版现金补差。save_context 提供原版加点浏览间隔与按存档归属的购买记录。

夜间使用 open_outside_confirmation 进入正常出发确认，地图中的 travel-* 对应已发现地点。转场期间 busy=true，到达后读取场景面板、商品、随身库存和正常按钮。afterhour_ground 是地上物品，返回前需转入正常随身槽位。return_store 使用原版返回与遗留物品确认，通用确认框提供 confirm_prompt / cancel_prompt。垃圾场、科技商店、食品商店的具体行为待中期副本实测。

image_targets 提供当前可查询的图像目标。inspect image 首次返回 image_status=pending，随后重复查询获取 image/png。图像来自游戏渲染帧的局部裁切，MCP 以图像内容返回，CLI JSON 包含 Base64 数据。当前目标包括可见顾客和已打开的通缉页。report_customer 要求 evidence，身份由模型结合可见资料判断。

## 项目文档

- [设计与验收约定](docs/DESIGN.md)
- [本轮准备结果与下一轮入口](docs/STATUS.md)
- [自动检查与实机验收步骤](docs/TESTING.md)

版本管理覆盖源码、脚本和文档。本机配置、游戏程序集、存档、日志、运行消息和构建产物通过忽略规则排除。构建与实机测试需要玩家本机已安装的受支持游戏。
