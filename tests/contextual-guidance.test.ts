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
