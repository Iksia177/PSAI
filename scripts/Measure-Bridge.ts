import { mkdir, mkdtemp, readdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { BridgeClient } from '../src/client/bridge.ts';

function distribution(values: number[]) {
  const ordered = [...values].sort((a, b) => a - b);
  return { samples: ordered.length, p50: ordered[Math.floor(ordered.length / 2)],
    p95: ordered[Math.min(ordered.length - 1, Math.floor(ordered.length * 0.95))] };
}

const live = process.argv.includes('--live');
const report: Record<string, unknown> = { measured_at: new Date().toISOString(), mode: live ? 'live_read_only' : 'offline_directory_proxy' };
if (live) {
  process.env.PSAI_PROFILE = '1';
  const client = new BridgeClient();
  if (!(await client.status()).ready) throw new Error('Load a playable save before live measurements.');
  const baseline = await client.send('observe');
  if (baseline.status !== 'completed') throw new Error('Observation failed.');
  const state = baseline.data as { inventories?: { items: { id: string }[] }[] };
  const target = state.inventories?.flatMap(inventory => inventory.items)[0]?.id;
  const measurements: Record<string, unknown> = {};
  for (const tool of ['observe', 'actions', 'inspect'] as const) {
    if (tool === 'inspect' && !target) continue;
    const receipts = [];
    for (let index = 0; index < 10; index++) {
      const receipt = await client.send(tool, tool === 'inspect' ? { target, detail: 'description' } : {});
      if (receipt.status !== 'completed') throw new Error('Read measurement failed: ' + receipt.error);
      receipts.push(receipt);
    }
    measurements[tool] = { round_trip_ms: distribution(receipts.map(receipt =>
      (receipt.client_performance as { round_trip_ms: number }).round_trip_ms)), receipts };
  }
  report.measurements = measurements;
  report.note = 'No game actions were executed. Action timings are recorded in normal act receipts and require authorized live gameplay.';
} else {
  await mkdir(resolve('.cache/performance'), { recursive: true });
  const root = await mkdtemp(resolve('.cache/performance/listing-'));
  const cases = [];
  for (const count of [20, 5000]) {
    const directory = join(root, String(count));
    await mkdir(directory);
    for (let index = 0; index < count; index++)
      await writeFile(join(directory, `result-session-${index}.json`), '{}');
    const samples = [];
    for (let index = 0; index < 30; index++) {
      const started = performance.now();
      const files = (await readdir(directory)).sort();
      files.filter(path => path.startsWith('request-') && path.endsWith('.json'));
      samples.push(performance.now() - started);
    }
    cases.push({ files: count, listing_ms: distribution(samples) });
  }
  report.cases = cases;
  report.note = 'Node directory enumeration proxy on this host. Unity/Mono ModHelper timings must be read from native performance.directory_scan_ms after loading the updated mod.';
}
await mkdir(resolve('artifacts/performance'), { recursive: true });
const path = resolve('artifacts/performance', `${live ? 'live' : 'offline'}-${Date.now()}.json`);
await writeFile(path, JSON.stringify(report, null, 2) + '\n');
process.stdout.write(JSON.stringify({ path, ...report }, null, 2) + '\n');
