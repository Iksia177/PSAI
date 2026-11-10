import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { BridgeClient, type Receipt, type Request } from '../src/client/bridge.ts';

// Explicit opt-in because this fixture performs real transactions and ends a day.
if (!process.argv.includes('--confirm-day-one')) {
  throw new Error('Use --confirm-day-one with the independent Chinese day-one hydroponic test save.');
}

interface Item { id: string; name: string; count: number }
interface Snapshot {
  ready: boolean; controlled: boolean; phase: string; day: number; cash: number; busy: boolean;
  customer: string; dialogue: { customer: string };
  actions: { action: string; label: string; enabled: boolean }[];
  inventories: { id: string; items: Item[] }[];
  screens: { id: string; text: string[] }[];
  control_options?: Record<string, boolean>;
}
interface Observation { receipt: Receipt; state: Snapshot }

const client = new BridgeClient();
const directory = join('artifacts', 'live', 'day-one-' + Date.now());
await mkdir(directory, { recursive: true });
const status = await client.status();
assert(status.ready && status.compatible, 'Bridge must be ready and compatible.');
const session = status.session_id;
let sequence = 0;

async function call(tool: Request['tool'], args: Record<string, unknown> = {}, options: {
  revision?: number; commandId?: string; session?: string;
} = {}): Promise<Receipt> {
  const receipt = await client.send(tool, args, options);
  await writeFile(join(directory, `${++sequence}-${tool}-${receipt.command_id}.json`), JSON.stringify(receipt, null, 2));
  assert.equal(receipt.session_id, session, 'Game was reloaded during the fixture.');
  assert.equal(receipt.status, 'completed', JSON.stringify(receipt));
  return receipt;
}

async function observe(): Promise<Observation> {
  const receipt = await call('observe');
  const state = receipt.data as Snapshot;
  assert(state.ready, 'The test save must remain loaded.');
  return { receipt, state };
}

function has(observation: Observation, action: string): boolean {
  return observation.state.actions.some(option => option.action === action && option.enabled);
}

async function act(action: string, args: Record<string, unknown> = {}): Promise<Receipt> {
  const observation = await observe();
  assert(has(observation, action), `Action unavailable: ${action}`);
  return call('act', { action, ...args }, { revision: observation.receipt.revision as number, session });
}

async function waitFor(predicate: (observation: Observation) => boolean, description: string): Promise<Observation> {
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    const observation = await observe();
    if (!observation.state.busy && predicate(observation)) return observation;
    await delay(300);
  }
  throw new Error(`Timed out waiting for ${description}. Evidence is in ${directory}`);
}

async function finishPlainDialogue(): Promise<Observation> {
  for (let line = 0; line < 100; line++) {
    const observation = await observe();
    const choices = observation.state.actions.filter(option => option.enabled && option.action.startsWith('choice-'));
    assert.equal(choices.length, 0, `Human review required for dialogue choices: ${JSON.stringify(choices)}`);
    if (observation.state.busy) { await delay(300); continue; }
    if (has(observation, 'dialogue_advance')) {
      console.log(observation.state.customer, observation.state.dialogue.customer);
      await act('dialogue_advance');
      await delay(300);
      continue;
    }
    if (has(observation, 'call_next') || has(observation, 'shutter') || has(observation, 'dismiss_customer')) return observation;
    await delay(300);
  }
  throw new Error('Dialogue did not reach an ordinary decision point.');
}

async function callCustomer(): Promise<Observation> {
  await act('call_next');
  return waitFor(observation => has(observation, 'dialogue_advance') && observation.state.dialogue.customer.length > 0, 'customer dialogue');
}

async function collectDocuments(): Promise<void> {
  const observation = await observe();
  const gifts = observation.state.inventories.find(inventory => inventory.id === 'counter')?.items
    .filter(item => item.name.startsWith('导师笔记') || item.name === '治安部通缉名单') ?? [];
  for (const item of gifts) await act('move_item', { item: item.id, destination: 'inventory', count: 1 });
}

async function clickLabel(pattern: RegExp): Promise<Receipt> {
  const observation = await observe();
  const buttons = observation.state.actions.filter(option => option.enabled && pattern.test(option.label));
  assert.equal(buttons.length, 1, `Expected one button for ${pattern}, found ${JSON.stringify(buttons)}`);
  return act(buttons[0]!.action);
}

async function closeTrade(): Promise<void> {
  const observation = await observe();
  if (observation.state.screens.some(screen => screen.id === 'trade')) await clickLabel(/^Close$/);
}

const initial = await observe();
assert.equal(initial.state.phase, 'MORNING');
assert.equal(initial.state.day, 1);
assert(initial.state.inventories.some(inventory => inventory.items.some(item => item.name === '水培农业手册')),
  'This fixture requires the prepared hydroponic start.');
const initialOptions = initial.state.control_options;
await act('begin_control');
await act('shutter');
await waitFor(observation => has(observation, 'call_next'), 'open shop');

const mentor = await callCustomer();
assert.equal(mentor.state.customer, '导师');
await finishPlainDialogue();
await collectDocuments();
const seller = await callCustomer();
assert.equal(seller.state.customer, '拾荒客');
await finishPlainDialogue();
let observation = await observe();
const food = observation.state.inventories.find(inventory => inventory.id === 'counter')?.items.find(item => item.name === '繁星果糊');
assert(food, 'Expected the visible day-one food offer.');
await act('open_trade');
await act('move_item', { item: food.id, destination: 'back_counter', count: 1 });
await waitFor(observation => observation.state.actions.some(option => option.enabled && option.label === '购买1物品'), 'buy button');
const beforeBuy = await observe();
const purchase = await clickLabel(/^购买1物品$/);
const bought = await waitFor(observation => observation.state.cash < beforeBuy.state.cash, 'purchase result');
const original = purchase.request!;
await call('act', original.arguments, { revision: original.expected_revision, session, commandId: original.command_id });
assert.equal((await observe()).state.cash, bought.state.cash, 'Duplicate command charged twice.');
await closeTrade();
await act('dismiss_customer');
await waitFor(observation => has(observation, 'call_next'), 'next customer');

const buyer = await callCustomer();
assert.equal(buyer.state.customer, '星际客');
await finishPlainDialogue();
observation = await observe();
const ownedFood = observation.state.inventories.find(inventory => inventory.id === 'inventory')?.items.find(item => item.name === '繁星果糊');
assert(ownedFood, 'Purchased food must be in the inventory.');
await act('open_trade');
await act('move_item', { item: ownedFood.id, destination: 'back_counter', count: 1 });
await waitFor(observation => observation.state.actions.some(option => option.enabled && option.label === '出售1物品'), 'sell button');
const beforeSell = await observe();
await clickLabel(/^出售1物品$/);
const sold = await waitFor(observation => observation.state.cash > beforeSell.state.cash, 'sale result');
await closeTrade();
await act('dismiss_customer');

for (let customer = 0; customer < 20; customer++) {
  observation = await waitFor(observation => has(observation, 'call_next') || has(observation, 'shutter'), 'queue decision');
  if (has(observation, 'shutter')) break;
  observation = await callCustomer();
  if (has(observation, 'dismiss_customer')) await act('dismiss_customer');
  else { await finishPlainDialogue(); await collectDocuments(); }
}

await act('shutter');
await waitFor(observation => observation.state.phase === 'CLOSED' && has(observation, 'open_nightly_report'), 'nightly report entry');
await act('open_nightly_report');
await waitFor(observation => observation.state.screens.some(screen => screen.id === 'nightly_report'), 'nightly report');
console.log('Nightly report', JSON.stringify((await observe()).state.screens));
await clickLabel(/睡觉|结束一[天日]|结束今天|Sleep|End Day/i);
const morning = await waitFor(observation => observation.state.day === 2, 'day two');
await act('end_control');
const restored = await observe();
assert.equal(restored.state.controlled, false);
if (initialOptions) assert.deepEqual(restored.state.control_options, initialOptions);
const summary = { session, initialCash: initial.state.cash, afterBuy: bought.state.cash, afterSell: sold.state.cash,
  nextDay: morning.state.day, nextDayCash: morning.state.cash, optionsRestored: true, evidenceDirectory: directory };
await writeFile(join(directory, 'summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));
