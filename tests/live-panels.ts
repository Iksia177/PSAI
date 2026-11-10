import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { BridgeClient, type Request } from '../src/client/bridge.ts';

// This fixture changes window state in the actual game. Item transfers are tested separately.
if (!process.argv.includes('--confirm-reversible-panels')) throw new Error('Use --confirm-reversible-panels with the independent test save.');

interface Panel { id: string; name: string; open: boolean; can_open: boolean; can_close: boolean; kind: string; inventories: string[] }
interface Snapshot {
  ready: boolean; busy: boolean; controlled: boolean; day: number; cash: number;
  panels: Panel[]; inventories: { id: string; panel: string | null }[];
  control_options: Record<string, boolean>;
}
const client = new BridgeClient();
const directory = join('artifacts', 'live', 'panels-' + Date.now());
await mkdir(directory, { recursive: true });
let sequence = 0;
async function call(tool: Request['tool'], args: Record<string, unknown> = {}, options = {}) {
  const receipt = await client.send(tool, args, options);
  await writeFile(join(directory, `${++sequence}-${tool}-${receipt.command_id}.json`), JSON.stringify(receipt, null, 2));
  return receipt;
}
async function observe() {
  const receipt = await call('observe');
  assert.equal(receipt.status, 'completed');
  const state = receipt.data as Snapshot;
  assert(state.ready && !state.busy, 'A stable loaded game is required.');
  assert(Array.isArray(state.panels), 'Install the panel bridge update first.');
  return { receipt, state };
}
async function act(action: string, target?: string) {
  const before = await observe();
  const receipt = await call('act', { action, ...(target ? { target } : {}) }, {
    session: before.receipt.session_id, revision: before.receipt.revision,
  });
  assert.equal(receipt.status, 'completed', JSON.stringify(receipt));
  return receipt;
}
const initial = await observe();
const results: { id: string; name: string; inventories: number; closedIsolation: boolean }[] = [];
try {
  if (!initial.state.controlled) await act('begin_control');
  for (const original of initial.state.panels) {
    if (!original.open && !original.can_open) continue;
    try {
      await act('open_panel', original.id);
      const repeated = await act('open_panel', original.id);
      assert.equal((repeated.data as { invoked: boolean }).invoked, false);
      const opened = await observe();
      assert.equal(opened.receipt.session_id, initial.receipt.session_id);
      const panel = opened.state.panels.find(panel => panel.id === original.id)!;
      assert(panel.open);
      const detail = await call('inspect', { target: original.id, detail: panel.kind === 'machine' ? 'machine' : 'contents' });
      assert.equal(detail.status, 'completed', JSON.stringify(detail));
      const content = detail.data as { id: string; items?: unknown[]; inventories?: { id: string }[] };
      const inventories = content.items ? [content] : content.inventories!;
      assert(inventories.length > 0, `No readable inventory in ${panel.name}`);
      assert.deepEqual(inventories.map(inventory => inventory.id), panel.inventories);
      let isolated = false;
      if (panel.can_close) {
        await act('close_panel', original.id);
        assert.equal(((await act('close_panel', original.id)).data as { invoked: boolean }).invoked, false);
        const closed = await observe();
        assert.equal(closed.state.panels.find(panel => panel.id === original.id)!.open, false);
        assert(closed.state.inventories.every(inventory => !panel.inventories.includes(inventory.id)));
        const denied = await call('inspect', { target: original.id, detail: 'contents' });
        assert.equal(denied.status, 'error');
        assert.equal(denied.error, 'panel_not_open');
        assert.equal(denied.data, undefined);
        isolated = true;
      }
      results.push({ id: original.id, name: original.name, inventories: inventories.length, closedIsolation: isolated });
    } finally {
      const current = (await observe()).state.panels.find(panel => panel.id === original.id);
      if (current && current.open !== original.open) await act(original.open ? 'open_panel' : 'close_panel', original.id);
    }
  }
  const missing = await call('inspect', { target: 'expired-panel', detail: 'contents' });
  assert.equal(missing.status, 'error');
  assert.equal(missing.error, 'panel_not_available');
} finally {
  if (!initial.state.controlled) await act('end_control');
}
const final = await observe();
assert.equal(final.state.day, initial.state.day);
assert.equal(final.state.cash, initial.state.cash);
assert.equal(final.state.controlled, initial.state.controlled);
assert.deepEqual(final.state.control_options, initial.state.control_options);
const summary = { session: initial.receipt.session_id, scope: 'Visible panel lifecycle only', results,
  day: final.state.day, cash: final.state.cash, optionsRestored: true };
await writeFile(join(directory, 'summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ ...summary, evidenceDirectory: directory }, null, 2));
