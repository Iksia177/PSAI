import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, readFile, writeFile, readdir, unlink } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { setTimeout as delay } from 'node:timers/promises';
import { BridgeClient, type Request } from '../src/client/bridge.ts';

async function fixture() {
  const root = resolve('.cache/tests');
  await mkdir(root, { recursive: true });
  const directory = await mkdtemp(join(root, 'bridge-'));
  const status = { protocol_version: '0.1', session_id: 'session-a', online: true, ready: true, compatible: true, updated_at: new Date().toISOString() };
  await writeFile(join(directory, 'status.json'), JSON.stringify(status));
  return { directory, status, client: new BridgeClient(directory, 250) };
}

// This peer exercises disk transport only. Gameplay acceptance requires the actual Unity process.
function peer(directory: string) {
  let closed = false;
  const seen = new Set<string>();
  const task = (async () => {
    while (!closed) {
      for (const file of await readdir(directory)) {
        if (!file.startsWith('request-') || !file.endsWith('.json') || seen.has(file)) continue;
        seen.add(file);
        const request = JSON.parse(await readFile(join(directory, file), 'utf8')) as Request;
        const result = { session_id: request.session_id, command_id: request.command_id, status: 'completed', request,
          data: { revision: 1, received: request.arguments } };
        await writeFile(join(directory, `result-${request.session_id}-${request.command_id}.json`), JSON.stringify(result));
        await unlink(join(directory, file));
      }
      await delay(10);
    }
  })();
  return { seen, stop: async () => { closed = true; await task; } };
}

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
    const { stdout } = await promisify(execFile)(process.execPath, ['src/client/cli.ts', 'observe', '--data-dir', directory]);
    assert.equal(JSON.parse(stdout).status, 'completed');
  } finally { await game.stop(); }
});

test('MCP stdio initializes, lists all six tools and calls bridge status', async () => {
  const { directory } = await fixture();
  const child = spawn(process.execPath, ['src/client/mcp.ts'], { env: { ...process.env, PSAI_DATA_DIR: directory }, stdio: ['pipe', 'pipe', 'pipe'] });
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
    return messages.get(id) as { result: { tools?: { name: string }[]; content?: { text: string }[] }; error?: unknown };
  };
  try {
    send({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'psai-tests', version: '0.1.0' } } });
    assert.equal((await wait(1)).error, undefined);
    send({ jsonrpc: '2.0', method: 'notifications/initialized' });
    send({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
    const tools = (await wait(2)).result.tools!;
    assert.deepEqual(tools.map(tool => tool.name).sort(), ['psai_act', 'psai_actions', 'psai_inspect', 'psai_observe', 'psai_result', 'psai_status']);
    send({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'psai_status', arguments: {} } });
    const response = await wait(3);
    assert.equal(JSON.parse(response.result.content![0]!.text).session_id, 'session-a');
  } finally { child.stdin.end(); child.kill(); }
});
