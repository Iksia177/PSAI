# PSAI

Probably Stolen Agent Interface，为 Steam 版《深空当铺：可能是偷的》提供 agent 游玩接口。

当前为实验性 v0.1，基础营业、容器、日结、水培、电话、加点、材料升级、液体加工与中期交易已有实机结果。第 5 版增加原版店门条件与外出次数、所有权字段与霰弹枪动作。常驻背包内部读取和配给站商品按钮关联已修复，等待重载验收。完整进度见 `docs/STATUS.md`。

## 已确认的方向

- 原生 C# 模组负责读取状态和执行正常游戏动作，外部 TypeScript CLI 与 MCP 适配器共用协议。
- 模组通过官方自有数据目录 API 交换 JSON，在 Unity 主线程处理游戏对象。
- 优先读取结构化接口与文字，图像按需查询。当前顾客的独立内部通缉身份字段已获用户授权读取，观察结果标记来源。
- 首版验收完成一个正常营业日，涵盖顾客、对话、库存、议价、交易与日结。

## 本机开发

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

Node.js 24 可以直接运行本项目的 TypeScript 入口。安装锁定依赖后执行检查与测试。

```powershell
npm ci --ignore-scripts --registry=https://registry.npmjs.org
npm run check
npm run build
npm test
node src/client/cli.ts status
node src/client/cli.ts observe
node src/client/cli.ts actions
```

CLI 默认使用当前用户的 `AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/psai`。可使用 `--data-dir` 或 `PSAI_DATA_DIR` 指定路径。`observe` 回执提供 `session_id` 和 `data.revision`，动作必须携带这两个值。

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

CLI 和 MCP 的 `status`、`observe` 返回 `guidance`，包含偏好版本和按 priority 从小到大排列的建议。第 5 版增加优先保留展示柜物品、每日读取日报、开店和夜间回店后收回柜台自有物品。周期交租、产权目标、晨间机器整理和其他建议继续提供，具体加工与经营流程由模型判断。

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

本仓库从本地 Git 开始记录开发。首轮功能验收后再配置 GitHub 仓库与推送。版本管理覆盖源码、脚本和文档，本机配置、游戏程序集、存档、日志、运行消息和构建产物通过忽略规则排除。
