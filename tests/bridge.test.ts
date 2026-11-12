import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, readFile, writeFile, readdir, unlink } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { setTimeout as delay } from 'node:timers/promises';
import { BridgeClient, bridgeErrorOutput, type Request } from '../src/client/bridge.ts';
import { loadGuidance } from '../src/client/guidance.ts';
import { contextualGuidance } from '../src/client/contextual-guidance.ts';

async function fixture() {
  const root = resolve('.cache/tests');
  await mkdir(root, { recursive: true });
  const directory = await mkdtemp(join(root, 'bridge-'));
  const status = { protocol_version: '0.1', session_id: 'session-a', online: true, ready: true, compatible: true, updated_at: new Date().toISOString() };
  await writeFile(join(directory, 'status.json'), JSON.stringify(status));
  return { directory, status, client: new BridgeClient(directory, 250) };
}

// This peer exercises disk transport only. Gameplay acceptance requires the actual Unity process.
function peer(directory: string, responseData = (request: Request): unknown => ({ revision: 1, received: request.arguments })) {
  let closed = false;
  const seen = new Set<string>();
  const task = (async () => {
    while (!closed) {
      for (const file of await readdir(directory)) {
        if (!file.startsWith('request-') || !file.endsWith('.json') || seen.has(file)) continue;
        seen.add(file);
        const request = JSON.parse(await readFile(join(directory, file), 'utf8')) as Request;
        const result = { session_id: request.session_id, command_id: request.command_id, status: 'completed', request,
          data: responseData(request) };
        await writeFile(join(directory, `result-${request.session_id}-${request.command_id}.json`), JSON.stringify(result));
        await unlink(join(directory, file));
      }
      await delay(10);
    }
  })();
  return { seen, stop: async () => { closed = true; await task; } };
}

test('observe derives guidance from its returned snapshot instead of status or previous observations', async () => {
  const { directory, client } = await fixture();
  let unlocked = false;
  const server = peer(directory, () => ({ revision: 1, outing: { unlocked, can_depart_now: unlocked } }));
  try {
    const first = await client.send('observe');
    assert.equal((first.guidance as { availability: { afterhours: { state: string } } }).availability.afterhours.state, 'locked');
    unlocked = true;
    const second = await client.send('observe');
    assert.equal((second.guidance as { availability: { afterhours: { state: string } } }).availability.afterhours.state, 'available');
  } finally { await server.stop(); }
});

test('prompt text is foregrounded on observe and disappears after the peer closes the window', async () => {
  const { directory, client } = await fixture();
  let open = true;
  const game = peer(directory, () => ({ revision: 1, attention: { has_pending_response: open, items: open ? [{
    screen_id: 'generic_confirmation', topic: 'display_case_timing', requires_response: true,
    text: ['营业后放入展示柜的物品将在次日生效。'], controls: [{ action: 'confirm_prompt', enabled: true }],
  }] : [] } }));
  try {
    const result = await client.send('observe');
    assert.deepEqual(result.attention, (result.data as { attention: unknown }).attention);
    assert.match(JSON.stringify(result.attention), /次日生效/);
    assert.equal((result.guidance as { focus: { kind: string } }).focus.kind, 'respond_to_prompt');
    open = false;
    const closed = await client.send('observe');
    assert.equal((closed.attention as { has_pending_response: boolean }).has_pending_response, false);
    assert.equal((closed.guidance as { focus: { kind: string } }).focus.kind, 'continue_available_tasks');
  } finally { await game.stop(); }
});

test('action and later result retrieval foreground snapshot prompts without losing original results', async () => {
  const { directory, status, client } = await fixture();
  const request: Request = { session_id: status.session_id, command_id: 'prompt-action', tool: 'act',
    arguments: { action: 'begin_control' }, expected_revision: 1 };
  const attention = { has_pending_response: true, items: [{ screen_id: 'dialogue_settings', requires_response: true,
    text: ['对白设置'], controls: [{ action: 'close_dialogue_settings', enabled: true }] }] };
  await writeFile(join(directory, `result-${status.session_id}-prompt-action.json`), JSON.stringify({
    session_id: status.session_id, command_id: 'prompt-action', status: 'completed', request,
    data: { invoked: true }, snapshot: { attention },
  }));
  const result = await client.send('act', request.arguments, { revision: 1, session: status.session_id, commandId: 'prompt-action' });
  assert.deepEqual(result.attention, attention);
  assert.deepEqual(result.data, { invoked: true });
  assert.deepEqual((await client.result('prompt-action')).attention, attention);
});

test('round trip preserves request identity and caller arguments', async () => {
  const { directory, client } = await fixture();
  const game = peer(directory);
  try {
    const result = await client.send('inspect', { target: 'item-1', detail: 'description' });
    assert.equal(result.status, 'completed');
    assert.deepEqual(result.data, { revision: 1, received: { target: 'item-1', detail: 'description' } });
    assert.equal(game.seen.size, 1);
  } finally { await game.stop(); }
});

test('not-ready saves require user handoff without publishing a request', async () => {
  const { directory, status, client } = await fixture();
  await writeFile(join(directory, 'status.json'), JSON.stringify({ ...status, ready: false }));
  assert.equal((await client.status()).recovery !== undefined, true);
  await assert.rejects(client.send('observe'), /game_not_ready/);
  assert.deepEqual(await readdir(directory), ['status.json']);
  for (const message of ['bridge_not_started: enable PSAI', 'bridge_offline', 'game_not_ready']) {
    const response = bridgeErrorOutput(new Error(message));
    assert.equal(response.error, message);
    assert.equal(response.recovery?.user_action_required, true);
    assert.match(response.recovery!.agent_instruction, /结束本轮/);
  }
  assert.equal(bridgeErrorOutput(new Error('stale_revision')).recovery, undefined);
});

test('concurrent equal command IDs publish one intent and return one receipt', async () => {
  const { directory, client } = await fixture();
  const game = peer(directory);
  try {
    const options = { commandId: 'same-command', revision: 1, session: 'session-a' };
    const results = await Promise.all([client.send('act', { action: 'call_next' }, options), client.send('act', { action: 'call_next' }, options)]);
    assert.deepEqual(results[0], results[1]);
    assert.equal(game.seen.size, 1);
    const repeated = await client.send('act', { action: 'call_next' }, options);
    assert.deepEqual(repeated, results[0]);
  } finally { await game.stop(); }
});

test('a reused ID with different arguments is rejected without replacing intent', async () => {
  const { client, directory } = await fixture();
  await client.send('act', { action: 'call_next' }, { commandId: 'conflict', revision: 1, session: 'session-a' });
  await assert.rejects(client.send('act', { action: 'dismiss_customer' }, { commandId: 'conflict', revision: 1, session: 'session-a' }), /command_id_conflict/);
  const intent = JSON.parse(await readFile(join(directory, 'intent-session-a-conflict.json'), 'utf8'));
  assert.equal(intent.arguments.action, 'call_next');
});

test('actions require revision and reject a stale session before creating an intent', async () => {
  const { client, directory } = await fixture();
  await assert.rejects(client.send('act', { action: 'shutter' }), /expected_revision_required/);
  await assert.rejects(client.send('act', { action: 'shutter' }, { revision: 1 }), /expected_session_required/);
  await assert.rejects(client.send('act', { action: 'shutter' }, { revision: 1, session: 'old-session' }), /stale_session/);
  assert.deepEqual(await readdir(directory), ['status.json']);
});

test('timeout returns an addressable pending receipt without inventing success', async () => {
  const { client } = await fixture();
  const pending = await client.send('observe', {}, { commandId: 'slow' });
  assert.equal(pending.status, 'pending');
  assert.equal(pending.command_id, 'slow');
  assert.equal((await client.result('slow', pending.session_id)).status, 'pending');
});

test('reload changes unresolved old receipts to uncertain and preserves completed receipts', async () => {
  const { client, directory, status } = await fixture();
  await writeFile(join(directory, 'result-session-a-done.json'), JSON.stringify({ session_id: 'session-a', command_id: 'done', status: 'completed' }));
  await writeFile(join(directory, 'result-session-a-pending.json'), JSON.stringify({ session_id: 'session-a', command_id: 'pending', status: 'pending' }));
  await writeFile(join(directory, 'status.json'), JSON.stringify({ ...status, session_id: 'session-b' }));
  assert.equal((await client.result('pending', 'session-a')).status, 'uncertain');
  assert.equal((await client.result('missing', 'session-a')).status, 'uncertain');
  assert.equal((await client.result('done', 'session-a')).status, 'completed');
});

test('orphaned intent can be published on retry', async () => {
  const { client, directory } = await fixture();
  const request = { session_id: 'session-a', command_id: 'orphan', tool: 'observe', arguments: {} };
  await writeFile(join(directory, 'intent-session-a-orphan.json'), JSON.stringify(request));
  const game = peer(directory);
  try { assert.equal((await client.send('observe', {}, { commandId: 'orphan' })).status, 'completed'); }
  finally { await game.stop(); }
});

test('partial receipt writes are retried', async () => {
  const { client, directory } = await fixture();
  const path = join(directory, 'result-session-a-partial.json');
  await writeFile(path, '{');
  const writer = (async () => { await delay(40); await writeFile(path, JSON.stringify({ session_id: 'session-a', command_id: 'partial', status: 'completed' })); })();
  assert.equal((await client.result('partial')).status, 'completed');
  await writer;
});

test('offline status, path traversal, oversized requests and mismatched receipts are rejected', async () => {
  const { client, directory, status } = await fixture();
  await assert.rejects(client.result('../escape'), /invalid_id/);
  await assert.rejects(client.send('inspect', { detail: 'a'.repeat(66000) }), /request_too_large/);
  await writeFile(join(directory, 'result-session-a-wrong.json'), JSON.stringify({ session_id: 'another-session', command_id: 'wrong', status: 'completed' }));
  await assert.rejects(client.result('wrong'), /receipt_identity_mismatch/);
  await writeFile(join(directory, 'status.json'), JSON.stringify({ ...status, updated_at: '2000-01-01T00:00:00Z' }));
  await assert.rejects(client.status(), /bridge_offline/);
  await unlink(join(directory, 'status.json'));
  await assert.rejects(client.status(), /bridge_not_started/);
});

test('CLI executes through its public entry point and emits JSON', async () => {
  const { directory } = await fixture();
  const game = peer(directory);
  try {
    const preferences = resolve('psai.preferences.example.json');
    const { stdout } = await promisify(execFile)(process.execPath, ['src/client/cli.ts', 'observe', '--data-dir', directory],
      { env: { ...process.env, PSAI_PREFERENCES_FILE: preferences } });
    assert.equal(JSON.parse(stdout).status, 'completed');
    assert.equal(JSON.parse(stdout).guidance.mode, 'advisory');
    assert.deepEqual(JSON.parse(stdout).guidance, contextualGuidance(await loadGuidance(preferences), JSON.parse(stdout).data));
  } finally { await game.stop(); }
});

test('MCP stdio initializes, lists all six tools and calls bridge status', async () => {
  const { directory, status } = await fixture();
  const pixel = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6G5sAAAAASUVORK5CYII=';
  const game = peer(directory, request => request.arguments.detail === 'image'
    ? { image_status: 'ready', image: { mime_type: 'image/png', data: pixel } }
    : { received: request.arguments });
  const preferences = resolve('psai.preferences.example.json');
  const child = spawn(process.execPath, ['src/client/mcp.ts'], {
    env: { ...process.env, PSAI_DATA_DIR: directory, PSAI_PREFERENCES_FILE: preferences }, stdio: ['pipe', 'pipe', 'pipe'],
  });
  let buffer = '';
  let stderr = '';
  const messages = new Map<number, unknown>();
  child.stdout.on('data', chunk => {
    buffer += chunk.toString();
    while (buffer.includes('\n')) {
      const index = buffer.indexOf('\n');
      const line = buffer.slice(0, index); buffer = buffer.slice(index + 1);
      if (line.trim()) { const response = JSON.parse(line); if (typeof response.id === 'number') messages.set(response.id, response); }
    }
  });
  child.stderr.on('data', chunk => { stderr += chunk.toString(); });
  const send = (message: unknown) => child.stdin.write(JSON.stringify(message) + '\n');
  const wait = async (id: number) => {
    const deadline = Date.now() + 5000;
    while (!messages.has(id)) {
      if (Date.now() > deadline || child.exitCode !== null) throw new Error('MCP response missing ' + stderr);
      await delay(10);
    }
    return messages.get(id) as { result: { instructions?: string; serverInfo?: { version: string }; tools?: { name: string }[]; content?: { text?: string; type?: string; data?: string }[] }; error?: unknown };
  };
  try {
    send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'psai-tests', version: '0.1.0' } } });
    const initialized = await wait(1);
    assert.equal(initialized.error, undefined);
    assert.equal(initialized.result.serverInfo?.version, '0.9.5-f');
    assert.match(initialized.result.instructions!, /guidance/);
    send({ jsonrpc: '2.0', method: 'notifications/initialized' });
    send({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
    const tools = (await wait(2)).result.tools!;
    assert.deepEqual(tools.map(tool => tool.name).sort(), ['psai_act', 'psai_actions', 'psai_inspect', 'psai_observe', 'psai_result', 'psai_status']);
    send({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'psai_status', arguments: {} } });
    const response = await wait(3);
    assert.equal(JSON.parse(response.result.content![0]!.text!).session_id, 'session-a');
    assert.equal(JSON.parse(response.result.content![0]!.text!).guidance.mode, 'advisory');
    assert.deepEqual(JSON.parse(response.result.content![0]!.text!).guidance, contextualGuidance(await loadGuidance(preferences)));
    send({ jsonrpc: '2.0', id: 4, method: 'tools/call', params: { name: 'psai_act', arguments: {
      action: 'open_panel', target: 'poster_stash', expected_revision: 1, session_id: 'session-a',
    } } });
    const action = JSON.parse((await wait(4)).result.content![0]!.text!);
    assert.equal(action.status, 'completed');
    assert.deepEqual(action.data.received, { action: 'open_panel', target: 'poster_stash' });
    send({ jsonrpc: '2.0', id: 5, method: 'tools/call', params: { name: 'psai_inspect', arguments: { detail: 'use_conditions', item: 'item-1', target: 'item-2' } } });
    assert.deepEqual(JSON.parse((await wait(5)).result.content![0]!.text!).data.received,
      { detail: 'use_conditions', item: 'item-1', target: 'item-2' });
    send({ jsonrpc: '2.0', id: 6, method: 'tools/call', params: { name: 'psai_act', arguments: {
      action: 'button-1', allow_cash: false, expected_revision: 1, session_id: 'session-a',
    } } });
    assert.deepEqual(JSON.parse((await wait(6)).result.content![0]!.text!).data.received, { action: 'button-1', allow_cash: false });
    send({ jsonrpc: '2.0', id: 7, method: 'tools/call', params: { name: 'psai_inspect', arguments: { detail: 'image', target: 'wanted_paper' } } });
    const image = (await wait(7)).result.content!;
    assert.equal(image[1]!.type, 'image');
    assert.equal(image[1]!.data, pixel);
    assert.equal(JSON.parse(image[0]!.text!).data.image.data, undefined);
    send({ jsonrpc: '2.0', id: 8, method: 'tools/call', params: { name: 'psai_act', arguments: {
      action: 'range-item-1', low: 2, high: 7, expected_revision: 1, session_id: 'session-a',
    } } });
    assert.deepEqual(JSON.parse((await wait(8)).result.content![0]!.text!).data.received,
      { action: 'range-item-1', low: 2, high: 7 });
    send({ jsonrpc: '2.0', id: 9, method: 'tools/call', params: { name: 'psai_act', arguments: {
      action: 'slider-item-1', value: 0.5, expected_revision: 1, session_id: 'session-a',
    } } });
    assert.deepEqual(JSON.parse((await wait(9)).result.content![0]!.text!).data.received,
      { action: 'slider-item-1', value: 0.5 });
    send({ jsonrpc: '2.0', id: 11, method: 'tools/call', params: { name: 'psai_act', arguments: {
      action: 'set_item_label_text', target: 'name_editor-1', text: '备用水瓶', expected_revision: 1, session_id: 'session-a',
    } } });
    assert.deepEqual(JSON.parse((await wait(11)).result.content![0]!.text!).data.received,
      { action: 'set_item_label_text', target: 'name_editor-1', text: '备用水瓶' });
    await writeFile(join(directory, 'status.json'), JSON.stringify({ ...status, ready: false, updated_at: new Date().toISOString() }));
    send({ jsonrpc: '2.0', id: 10, method: 'tools/call', params: { name: 'psai_observe', arguments: {} } });
    const recovery = JSON.parse((await wait(10)).result.content![0]!.text!);
    const cli = await promisify(execFile)(process.execPath, ['src/client/cli.ts', 'observe', '--data-dir', directory])
      .catch(error => ({ stdout: error.stdout as string }));
    assert.deepEqual(JSON.parse(cli.stdout), recovery);
    assert.equal(recovery.recovery.user_action_required, true);
  } finally { child.stdin.end(); child.kill(); await game.stop(); }
});
