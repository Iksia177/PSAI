import assert from 'node:assert/strict';
import { test } from 'node:test';
import { loadGuidance } from '../src/client/guidance.ts';
import { contextualGuidance } from '../src/client/contextual-guidance.ts';

test('first-day guidance postpones locked systems and absent machines without diagnosing save failure', async () => {
  const result = contextualGuidance(await loadGuidance(''), {
    day: 1, location: 'store', progression: { network_introduced: false, display_case_introduced: false },
    outing: { unlocked: false, can_depart_now: false }, actions: [], panels: [],
  });
  assert.equal(result.availability.afterhours.state, 'locked');
  assert.equal(result.availability.upgrades.state, 'locked');
  assert.equal(result.availability.machines.state, 'not_observed');
  for (const id of ['night-outing', 'outing-carry-check', 'upgrade-review', 'morning-machines', 'showcase-purpose']) {
    assert.ok(!result.rules.some(rule => rule.id === id));
    assert.ok(result.deferred_rules.some(rule => rule.id === id));
  }
  assert.ok(result.rules.some(rule => rule.id === 'progression-first'));
  assert.equal(result.user_instructions_take_precedence, true);
});

test('ordinary telephone availability does not unlock the upgrade network', async () => {
  const result = contextualGuidance(await loadGuidance(''), {
    progression: { network_introduced: false }, actions: [{ action: 'open_phone', enabled: true }],
    outing: { unlocked: true, can_depart_now: false, blocked_reasons: ['需要先结束营业'] },
  });
  assert.equal(result.availability.telephone.state, 'available');
  assert.equal(result.availability.upgrades.state, 'locked');
  assert.equal(result.availability.afterhours.state, 'temporarily_unavailable');
  assert.match(result.availability.afterhours.reason, /结束营业/);
});

test('current progression enables rules regardless of game day and a reload does not reuse unlocks', async () => {
  const defaults = await loadGuidance('');
  const ready = contextualGuidance(defaults, {
    day: 1, progression: { network_introduced: true, display_case_introduced: true },
    actions: [{ action: 'open_phone', enabled: true }], outing: { unlocked: true, can_depart_now: true },
    panels: [{ kind: 'machine' }],
  });
  for (const id of ['night-outing', 'upgrade-review', 'showcase-purpose', 'morning-machines'])
    assert.ok(ready.rules.some(rule => rule.id === id));
  const reloaded = contextualGuidance(defaults, { day: 30, progression: { network_introduced: false }, outing: { unlocked: false } });
  assert.equal(reloaded.availability.upgrades.state, 'locked');
  assert.ok(!reloaded.rules.some(rule => rule.id === 'night-outing'));
});

test('status and older snapshots preserve unknown states instead of inferring unlocks from supported features', async () => {
  const defaults = await loadGuidance('');
  const status = contextualGuidance(defaults);
  assert.equal(status.context, 'awaiting_observation');
  assert.equal(status.focus.kind, 'observe_first');
  assert.equal(status.availability.upgrades.state, 'unknown');
  const old = contextualGuidance(defaults, { features: ['afterhour', 'telephone'], day: 50 });
  assert.equal(old.availability.afterhours.state, 'unknown');
  assert.ok(!old.rules.some(rule => rule.id === 'upgrade-review'));
});

test('current visible activity keeps relevant advice and local custom instructions remain intact', async () => {
  const defaults = await loadGuidance('');
  defaults.rules.push({ id: 'custom-user-policy', priority: 1, instruction: 'Report decisions to the user.' });
  const outside = contextualGuidance(defaults, { location: 'dumping_ground', outing: { unlocked: true, can_depart_now: false } });
  assert.ok(outside.rules.some(rule => rule.id === 'outing-carry-check'));
  assert.ok(outside.rules.some(rule => rule.id === 'custom-user-policy'));
  const report = contextualGuidance(defaults, { screens: [{ id: 'nightly_report' }] });
  assert.ok(report.rules.some(rule => rule.id === 'read-daily-report'));
  assert.ok(!contextualGuidance(defaults, { screens: [] }).rules.some(rule => rule.id === 'read-daily-report'));
});

test('response windows take focus while tutorial-only hints remain informational', async () => {
  const defaults = await loadGuidance('');
  const tutorial = { screen_id: 'tutorial_drag', requires_response: false, blocks_gameplay: null };
  const settings = { screen_id: 'dialogue_settings', requires_response: true, blocks_gameplay: null };
  const pending = contextualGuidance(defaults, { attention: { items: [tutorial, settings] } });
  assert.equal(pending.focus.kind, 'respond_to_prompt');
  assert.deepEqual(pending.focus.screen_ids, ['dialogue_settings']);
  assert.ok(pending.rules.some(rule => rule.id === 'prompt-response'));
  const hints = contextualGuidance(defaults, { attention: { items: [tutorial] } });
  assert.equal(hints.focus.kind, 'read_tutorial_hint');
  assert.match(hints.focus.instruction, /不要求立即关闭/);
  const empty = contextualGuidance(defaults, { attention: { items: [] } });
  assert.ok(!empty.rules.some(rule => rule.id === 'prompt-response'));
});

test('event choices outrank routine customer actions and disabled choices are not recommended', async () => {
  const result = contextualGuidance(await loadGuidance(''), {
    customer_interaction: { kind: 'event_dialogue', primary_action_ids: ['choice-0', 'choice-1'], secondary_action_ids: ['dismiss_customer', 'open_trade'] },
    actions: [{ action: 'choice-0', enabled: true }, { action: 'choice-1', enabled: false },
      { action: 'dismiss_customer', enabled: true }, { action: 'open_trade', enabled: true }],
  });
  assert.equal(result.focus.kind, 'respond_to_customer');
  assert.deepEqual(result.focus.primary_action_ids, ['choice-0']);
  assert.match(result.focus.instruction, /不是事件 choice 的替代/);
});

test('a native confirmation takes precedence over event choices without losing the event', async () => {
  const result = contextualGuidance(await loadGuidance(''), {
    customer_interaction: { kind: 'event_dialogue', primary_action_ids: ['choice-0'] },
    actions: [{ action: 'choice-0', enabled: true }],
    attention: { items: [{ screen_id: 'generic_confirmation', requires_response: true,
      controls: [{ action: 'confirm_prompt', enabled: true }, { action: 'cancel_prompt', enabled: true }] }] },
  });
  assert.equal(result.focus.kind, 'respond_to_prompt');
  assert.deepEqual(result.focus.primary_action_ids, ['confirm_prompt', 'cancel_prompt']);
});

test('older native mods still prioritize visible choices, while ordinary trades keep their workflow', async () => {
  const defaults = await loadGuidance('');
  const old = contextualGuidance(defaults, { actions: [{ action: 'choice-2', enabled: true }, { action: 'dismiss_customer', enabled: true }] });
  assert.equal(old.focus.kind, 'respond_to_customer');
  assert.deepEqual(old.focus.primary_action_ids, ['choice-2']);
  const trade = contextualGuidance(defaults, { customer_interaction: { kind: 'trade', primary_action_ids: ['open_trade'] }, actions: [{ action: 'open_trade', enabled: true }] });
  assert.equal(trade.focus.kind, 'continue_available_tasks');
});

test('an opened guide is read before answering the waiting customer, then customer focus resumes', async () => {
  const defaults = await loadGuidance('');
  const base = { customer_interaction: { kind: 'dialogue_choice', primary_action_ids: ['choice-0'] }, actions: [{ action: 'choice-0', enabled: true }] };
  const reading = contextualGuidance(defaults, { ...base, documents: [{ id: 'multipage_guide', page_number: 1, text: ['机械手鉴定指南'] }] });
  assert.equal(reading.focus.kind, 'read_document');
  assert.deepEqual(reading.focus.document_ids, ['multipage_guide']);
  assert.deepEqual(reading.focus.primary_action_ids, []);
  assert.ok(reading.rules.some(rule => rule.id === 'read-open-documents'));
  const closed = contextualGuidance(defaults, { ...base, documents: [] });
  assert.equal(closed.focus.kind, 'respond_to_customer');
  assert.ok(!closed.rules.some(rule => rule.id === 'read-open-documents'));
});

test('a confirmation remains ahead of a graphical guide without visible body text', async () => {
  const result = contextualGuidance(await loadGuidance(''), {
    documents: [{ id: 'multipage_guide', text: [], text_status: 'no_visible_body_text', image_target: 'multipage_guide' }],
    attention: { items: [{ screen_id: 'generic_confirmation', requires_response: true, controls: [{ action: 'confirm_prompt', enabled: true }] }] },
  });
  assert.equal(result.focus.kind, 'respond_to_prompt');
  assert.deepEqual(result.focus.primary_action_ids, ['confirm_prompt']);
  assert.ok(result.rules.some(rule => rule.id === 'read-open-documents'));
});
test('tool result windows take focus and their enabled controls are delivered', async () => {
  const defaults = await loadGuidance('');
  const result = contextualGuidance(defaults, {
    attention: { items: [{ screen_id: 'plant_analyser-1', kind: 'tool_result', requires_response: true,
      controls: [{ action: 'close_plant_analyser', enabled: true }, { action: 'unavailable_match', enabled: false }] }] },
    customer_interaction: { kind: 'trade', primary_action_ids: ['open_trade'] },
    actions: [{ action: 'open_trade', enabled: true }],
  });
  assert.equal(result.focus.kind, 'respond_to_prompt');
  assert.deepEqual(result.focus.primary_action_ids, ['close_plant_analyser']);
  assert.match(result.focus.instruction, /tool_results/);
  assert.ok(result.rules.some(rule => rule.id === 'read-tool-results'));
  const closed = contextualGuidance(defaults, { attention: { items: [] } });
  assert.ok(!closed.rules.some(rule => rule.id === 'read-tool-results'));
});
