import { readFile } from 'node:fs/promises';
import { z } from 'zod';

const guidanceSchema = z.object({
  version: z.string().min(1),
  rules: z.array(z.object({
    id: z.string().min(1),
    priority: z.number().int().nonnegative(),
    instruction: z.string().min(1),
  })).refine(rules => new Set(rules.map(rule => rule.id)).size === rules.length, 'Duplicate rule IDs'),
});

export const defaultGuidance = {
  version: '10',
  rules: [
    { id: 'prompt-response', priority: 0, instruction: '每次观察或动作后先检查回执顶层 attention 和 guidance.focus。requires_response=true 的确认或设置窗口应先阅读内容并选择当前 controls 中启用的动作，再重新观察确认窗口消失或状态已推进；若 busy=true 或控件全部禁用，先等待并重新观察，不循环调用。确认可能消费、提交或推进剧情，不要自动点确定。设置窗口优先使用原版关闭按钮或对应 close_* 动作，不为关闭而改动设置。tutorial_hint 通常是说明或操作提示，按内容完成原版步骤；没有关闭动作时继续相应任务，不调用统一关闭教程。blocks_gameplay=null 表示尚不确定是否阻塞，不能把普通提示一律当作游戏卡死。' },
    { id: 'progression-first', priority: 0, instruction: '先读当前 observe 的 progression、guidance.availability、actions 和对白。status.features 表示模组支持能力，不表示存档已解锁；guidance.rules 是本次适用建议，deferred_rules 暂缓执行。教程或早期存档优先完成当前可用对白与营业，未开放或暂时受限的功能正常跳过，不循环尝试，不因此要求修复存档。状态未知时先观察和核对原版提示，动作与实际可见条件持续冲突时报告接口问题。当前用户指令优先。' },
    { id: 'read-effects', priority: 0, instruction: '移动、拆卸、升级或使用物品前，先读取相关物品、目标与设施的 tooltip 标签、效果和操作条件。根据 use_conditions 及原版提示选择工具和材料。' },
    { id: 'window-discipline', priority: 1, instruction: '优先只保留当前存取或使用所需的两个可关闭容器窗口。同一对容器可连续操作，完成或失败后关闭相关窗口。嵌套操作保留必要父窗口，结束时逆序关闭。' },
    { id: 'showcase-purpose', priority: 2, instruction: '展示柜具有影响客户群体等经营功能，优先保留其中物品用于陈列，交易先使用库存及其他自有储备。需要调整陈列时先读取客户群体与效果，再结合经营目的决定；优先开店前完成陈列，营业后调整可能次日才生效，出现 display_case_timing 提示时读取原版说明后确认。普通收纳优先选择用途合适的储物容器。' },
    { id: 'rent-and-property', priority: 3, instruction: '关注 obligations 和租金提示，优先保障周期交租及必要支出，避免连续两次交租失败导致游戏失败。以通过正常购房事件取得店铺产权作为长期目标，结合租金、经营与购房条件安排资金。' },
    { id: 'discover-operations', priority: 4, instruction: '优先使用结构化接口和文字发现操作。读取 tooltip、operations、use_conditions 和机器控件，自行学习加工与生产流程，按执行后的实际状态核对结果。图像仅在结构化信息和文字不足、有可疑线索或用户要求时按需查询。' },
    { id: 'morning-machines', priority: 5, instruction: '起床后优先检查机器，收集并处理产物，补充合适投入物。结合可见电量、兼容性、运行状态和剩余资源，优先换上电量更多的适用电池，保留必要供电。' },
    { id: 'read-daily-report', priority: 6, instruction: '每日先读取日报的夜间结果、事件线索、服务和状态信息，结合当前任务安排操作，读取完成后关闭报告。' },
    { id: 'counter-cleanup', priority: 7, instruction: 'counter 是门外的前柜台（顾客放货处）；back_counter 是店内交易备货台，不要把两者或外出地点的 afterhour_ground（地面）混为一谈。优先在打烊前、外出前，以及开店前后、每次交易前和成交或取消后整理自有台面物品。读取最新 observe.inventories 中这两个 id 的 items，依据 owned 或 tooltip 的“未拥有”标识核对所有权，再用 move_item（item=物品条目的 id，destination=库存 id）将自有遗留物移至 inventory 或已打开的合适储物容器，重新 observe 确认来源和去向。counter 上的物品在出门时会被原版清除，包括自有物品和准备携带的背包；关门不会自动收纳，窗口未列出也不代表台面已空，应优先在打烊前完成整理。交易前先整理再备货，保留当前交易中正在报价、加工或提交的物品，结束后再收尾；容量不足时选择用途合适的容器，所有权不明时先查询。' },
    { id: 'user-progress-reports', priority: 8, instruction: '推荐每天开店前（读取日报、整理库存与机器后）、每次确认实际成交后、夜间外出回店后，向用户简短回报已完成的操作、实际结果、选择理由和接下来打算做的事，方便用户及时指导。成交以现金、归属及原版结果复核，不把处理函数返回直接当作成交。用户可以调整回报频率；日常回报无需等待用户批准后续已授权操作。' },
    { id: 'trade-context', priority: 9, instruction: '交易先读 customer_context、dialogue.full_text、trade 和当前窗口文字。现金出售候选从 trade.sell_candidates 或已观察库存中的实际物品选择。以物易物时读取 trade.barter 的条件、候选、报价组及换得物，候选匹配单项条件不代表完整交换已满足，结合 conditions_met 与原版按钮判断。使用自定义回调且候选判断未知时，按当前面板与标签核对，不把未知当作拒收。备货放入 trade.offer_inventory（back_counter，交易备货台），再核对原版报价。counter 是前柜台，物品移动成功也不等于已加入报价。需求、预算或选择条件不满足时读取原版拒绝信息。' },
    { id: 'file-documents', priority: 10, instruction: '优先将已归自己所有、暂时不用的文件放入文档柜。' },
    { id: 'contact-cards', priority: 12, instruction: '查看名片时优先读取 contact_cards 中的号码与面板文字，完成后关闭对应名片弹窗。需要联系时正常拨号，首次拨打后再核对原版联系历史。' },
    { id: 'store-idle-items', priority: 20, instruction: '优先将闲置自有物品整理至按用途适合且能够接收的容器，优先利用储物柜。正在交易、展示或生产使用的物品保留相应用途，先为机器运行计划保留投入物，缺少合适容器或容量时自行选择临时位置。' },
    { id: 'run-machines', priority: 30, instruction: '优先使具备运行条件的机器正常运转，结合可见说明、现有材料与当前目标，自行选择投入物及投入顺序，并安排正常运行所需的材料和供电。' },
    { id: 'night-electricity', priority: 40, instruction: '按实际运行需求建议夜间用电。仅在计划运行的设备需要夜间外部供电时考虑订电，结合当前可见的供电、储能与消耗条件判断。信息不足时先查询设备，避免仅按持有电器作决定。' },
    { id: 'night-outing', priority: 45, instruction: '先检查 outing.unlocked、blocked_reasons、visits_remaining、can_depart_now 与门的提示。尚未解锁时跳过外出；条件不满足时按原因处理或等待正常进展，不反复尝试。条件允许时每晚优先考虑出门，结合当前目标、租金储备、时间和可见风险选择活动。外出选包优先选择当前允许携带、绝对总容量最大的背包，比较 capacity.total_cells，不以剩余空间代替总容量，同容量沿用列表顺序。' },
    { id: 'outing-carry-check', priority: 46, instruction: '外出选包前，先按 counter-cleanup 将 counter 上准备携带的背包及其他自有物品收回 inventory 或合适的店内容器。使用 observe.screens 中 id=response_closable 的 choices 实际返回的 action 选择背包，比较 capacity.total_cells；“已选背包”仅表示选择结果，不代表已经携带成功。抵达地点并等待 busy=false 后，读取 observe.inventories 中 afterhour_backpack（背包槽）的 items，并核对对应 owner_item 的 afterhour_backpack_contents 及物品数量；缺失或为空时优先排查并向用户回报，不宣布背包已成功带出。afterhour_ground 是外出地点的地面，返回前用 move_item 将需要带回的物品放入 afterhour_left、afterhour_right 或实际携带的背包内部库存，再观察确认。回店转场结束后检查 back_counter、inventory 和必要时 display_case 中的背包，再打开核对内容；完成整理后关闭可关闭窗口。' },
    { id: 'upgrade-review', priority: 50, instruction: '默认约每4个游戏日检查王德尔加点，可结合事件调整为3至5日。参考当前存档 save_context.upgrade_days_since_review 和购买记录，结合已有构筑与前置条件，约按1个主动构筑项目、2个被动项目交替考虑。默认仅使用专用货币，明确决定使用现金补差时传 allow_cash=true。' },
    { id: 'accept-events', priority: 60, instruction: 'NPC事件在资金、物品与前置条件满足并符合当前目标时，优先接受、购买或提交。读取完整条件与后续确认，资源冲突时结合用户目标自行取舍。' },
    { id: 'wanted-evidence', priority: 70, instruction: '优先读取 customer_identity 的独立通缉字段及名单关联，并结合文字线索判断举报依据。区分通缉犯与普通盗窃、出售赃物或违禁品等情况，使用 report_customer 时填写 evidence 和信息来源。结构化身份由用户授权读取。只有仍存在有意义的疑点且文字不足时才按需取图。' },
  ],
};

export async function loadGuidance(path = process.env.PSAI_PREFERENCES_FILE) {
  let configuration = defaultGuidance;
  let error: string | undefined;
  if (path) {
    try { configuration = guidanceSchema.parse(JSON.parse(await readFile(path, 'utf8'))); }
    catch { error = 'preferences_invalid_or_unreadable'; }
  }
  return { ...configuration, mode: 'advisory' as const, user_instructions_take_precedence: true,
    source: path && !error ? 'local' : 'default',
    rules: configuration.rules.map(rule => ({ ...rule })).sort((a, b) => a.priority - b.priority),
    ...(error ? { error } : {}),
  };
}
