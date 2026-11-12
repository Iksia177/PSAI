import type { loadGuidance } from './guidance.ts';

type Guidance = Awaited<ReturnType<typeof loadGuidance>>;
type State = 'available' | 'locked' | 'temporarily_unavailable' | 'not_observed' | 'unknown';
type Availability = { state: State; reason: string };
const available: Availability = { state: 'available', reason: '当前观察满足指导的适用条件，具体动作仍按原版条件检查。' };
const unknown: Availability = { state: 'unknown', reason: '尚缺少当前状态，先 observe；不要据此判断存档异常。' };
const object = (value: unknown): Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
const array = (value: unknown): Record<string, unknown>[] => Array.isArray(value) ? value.map(object) : [];

export function contextualGuidance(guidance: Guidance, snapshot?: unknown) {
  const view = object(snapshot);
  const observed = snapshot !== undefined;
  const progression = object(view.progression);
  const outing = object(view.outing);
  const actions = array(view.actions);
  const hasAction = (id: string) => actions.some(action => action.action === id && action.enabled === true);
  const screens = array(view.screens);
  const inventories = array(view.inventories);
  const panels = array(view.panels);
  const documents = array(view.documents);
  const prompts = array(object(view.attention).items);
  const pending = prompts.filter(prompt => prompt.requires_response === true);
  const interaction = object(view.customer_interaction);
  const primary = Array.isArray(interaction.primary_action_ids) ? interaction.primary_action_ids.filter(id =>
    typeof id === 'string' && hasAction(id)) : actions.filter(action =>
      typeof action.action === 'string' && action.action.startsWith('choice-') && action.enabled === true).map(action => action.action);
  const respond = interaction.kind === 'event_dialogue' || interaction.kind === 'dialogue_choice' || interaction.kind === 'dialogue' ||
    actions.some(action => typeof action.action === 'string' && action.action.startsWith('choice-'));
  const absent = (reason: string): Availability => ({ state: 'not_observed', reason });
  const unlocked = (flag: unknown): Availability => flag === true ? available : flag === false
    ? { state: 'locked', reason: '原版进度尚未开放，跳过此项，正常推进当前对白和营业。' } : unknown;
  const phone: Availability = hasAction('open_phone') || hasAction('dial_phone') ? available
    : !observed ? unknown : { state: 'temporarily_unavailable', reason: '当前没有可用电话入口；可能尚未出现、正处于对白或场景受限，不作为存档异常。' };
  let afterhours = unlocked(outing.unlocked ?? progression.afterhours_introduced);
  if (afterhours.state === 'available' && outing.can_depart_now !== true)
    afterhours = { state: 'temporarily_unavailable', reason: arrayReasons(outing.blocked_reasons) || '外出已开放，但当前阶段、次数或原版条件不允许出发。' };
  const network = unlocked(progression.network_introduced);
  const machines = !observed ? unknown : panels.some(panel => panel.kind === 'machine') ? available
    : absent('当前未观察到机器面板；跳过例行机器任务，获得或发现机器后再处理。');
  const customer = object(view.customer_context).arrived === true;
  const outside = typeof view.location === 'string' && view.location !== 'store';
  const choosingBag = screens.some(screen => screen.id === 'response_closable' && array(screen.choices).some(choice => choice.backpack !== undefined));
  const availability: Record<string, Availability> = {
    telephone: phone,
    afterhours,
    upgrades: network.state === 'available' ? phone : network,
    display_case: unlocked(progression.display_case_introduced),
    visual_inspection: unlocked(progression.visual_inspection_introduced),
    appraisal: unlocked(progression.appraisal_introduced),
    shooting: unlocked(progression.shooting_unlocked),
    machines,
    reports: !observed ? unknown : screens.some(screen => ['nightly_report', 'morning_report', 'calendar_report', 'daily_news'].includes(String(screen.id)))
      ? available : absent('当前没有打开的报告；出现报告时读取，不为执行此指导强行打开尚不可用的界面。'),
    customer: !observed ? unknown : customer ? available : absent('当前没有已到达的顾客，等待或处理其他可用任务。'),
    cards: !observed ? unknown : array(view.contact_cards).length ? available : absent('当前没有打开的名片；空列表不表示物品丢失或存档异常。'),
    documents: !observed ? unknown : inventories.some(inv => inv.id === 'documents') ? available : absent('当前未观察到文档柜入口，暂缓专门归档。'),
    storage: !observed ? unknown : panels.some(panel => panel.kind === 'container' && panel.owner_item && (panel.can_open === true || panel.open === true))
      ? available : absent('当前未观察到可用储物容器，保留物品在主库存并正常推进。'),
    carrying: outside || choosingBag ? available : afterhours,
    prompts: !observed ? unknown : prompts.length ? available : absent('当前没有已观察到的待处理提示或设置窗口。'),
    documents_open: !observed ? unknown : documents.length ? available : absent('当前没有打开的指南或书册，取得并正常打开后再阅读。'),
  };
  const dependencies: Record<string, string> = {
    'showcase-purpose': 'display_case', 'morning-machines': 'machines', 'run-machines': 'machines', 'night-electricity': 'machines',
    'read-daily-report': 'reports', 'night-outing': 'afterhours', 'outing-carry-check': 'carrying', 'upgrade-review': 'upgrades',
    'trade-context': 'customer', 'accept-events': 'customer', 'wanted-evidence': 'customer',
    'contact-cards': 'cards', 'file-documents': 'documents', 'store-idle-items': 'storage',
    'prompt-response': 'prompts',
    'read-open-documents': 'documents_open',
  };
  const rules: Guidance['rules'] = [];
  const deferred: { id: string; priority: number; state: State; reason: string }[] = [];
  for (const rule of guidance.rules) {
    const dependency = dependencies[rule.id];
    const condition = dependency ? availability[dependency]! : available;
    if (condition.state === 'available') rules.push(rule);
    else deferred.push({ id: rule.id, priority: rule.priority, ...condition });
  }
  return { ...guidance, context: observed ? 'current_observation' : 'awaiting_observation',
    focus: { kind: !observed ? 'observe_first' : pending.length ? 'respond_to_prompt' : documents.length ? 'read_document' : respond ? 'respond_to_customer' : prompts.length ? 'read_tutorial_hint' : 'continue_available_tasks',
      document_ids: documents.map(document => document.id),
      primary_action_ids: pending.length ? pending.flatMap(prompt => array(prompt.controls)
        .filter(control => control.enabled === true && typeof control.action === 'string').map(control => control.action)) : documents.length ? [] : respond ? primary : [],
      screen_ids: (pending.length ? pending : prompts).map(prompt => prompt.screen_id),
      instruction: !observed ? '先 observe 获取当前窗口、对白和可用动作。' : pending.length ? '先读当前提示或设置窗口，按内容选择正常动作；不要自动确认消费、提交或剧情决定。'
        : documents.length ? '读取当前打开文档的正文和页码；按返回动作翻页，需要图案时按需取图，阅读结束关闭。'
        : respond ? '优先完成当前对白及事件选项，读取 customer_interaction 与原版条件；送客、交易或砍价不是事件 choice 的替代操作。没有启用的主要动作时先等待并观察，不盲目改用送客。'
        : prompts.length ? '阅读教程提示，按当前实际操作推进；非阻塞说明不要求立即关闭。' : '按当前开放状态继续可用任务。' },
    rules, deferred_rules: deferred, availability,
    interpretation: 'features 表示模组实现的能力，不代表当前存档已开放。只将 rules 作为当前建议；deferred_rules 暂缓执行。动作缺失、未解锁或受限时重新观察并继续可用任务，单凭这些情况不要判定存档损坏。未知状态不等于未解锁。' };
}

function arrayReasons(value: unknown) {
  return Array.isArray(value) ? value.filter(reason => typeof reason === 'string').join('；') : '';
}
