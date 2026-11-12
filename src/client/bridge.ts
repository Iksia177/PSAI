import { randomUUID } from 'node:crypto';
import { readFile, writeFile, access, mkdir, rename, unlink } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { loadGuidance } from './guidance.ts';
import { contextualGuidance } from './contextual-guidance.ts';

export interface BridgeStatus {
  protocol_version: '0.1';
  session_id: string;
  online: boolean;
  ready: boolean;
  compatible: boolean;
  updated_at: string;
  [key: string]: unknown;
}

export interface Request {
  session_id: string;
  command_id: string;
  tool: 'observe' | 'inspect' | 'actions' | 'act';
  arguments: Record<string, unknown>;
  expected_revision?: number;
}

export interface Receipt {
  session_id: string;
  command_id: string;
  status: 'pending' | 'completed' | 'error' | 'uncertain';
  request?: Request;
  error?: string;
  [key: string]: unknown;
}

function deliverAttention(receipt: Receipt): Receipt {
  const data = receipt.data as { attention?: unknown } | undefined;
  const snapshot = receipt.snapshot as { attention?: unknown } | undefined;
  const view = data?.attention ? data : snapshot;
  return view?.attention ? { attention: view.attention, ...receipt } : receipt;
}

export function defaultDataDirectory(): string {
  return process.env.PSAI_DATA_DIR ?? join(homedir(), 'AppData', 'LocalLow', 'Questing Goose Studio', 'Probably Stolen', 'ModData', 'psai');
}

function recoveryFor(message: string) {
  const code = message.split(':')[0] ?? message;
  const steps: Record<string, string> = {
    bridge_not_started: '请用户安装并启用 PSAI 模组，重启游戏，然后载入存档并确认已进入可操作界面。',
    bridge_offline: '请用户启动游戏并载入存档，确认 PSAI 已启用；若刚退出游戏，等待用户重新载入。',
    game_not_ready: '请用户载入存档，进入店铺可操作界面后再继续。',
  };
  return steps[code] ? { user_action_required: true, message: steps[code],
    agent_instruction: '保存当前进度，明确告知用户所需操作并结束本轮。收到用户就绪确认后再检查桥接，避免循环重试或修改游戏存档、设置。' } : undefined;
}

export function bridgeErrorOutput(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  const recovery = recoveryFor(message);
  return { status: 'error', error: message, ...(recovery ? { recovery } : {}) };
}

function safeId(value: string): string {
  if (!/^[a-z0-9-]{1,64}$/.test(value)) throw new Error('invalid_id');
  return value;
}

async function readJson(path: string): Promise<unknown> {
  // The game helper writes directly. A reader can briefly encounter a partial JSON document.
  for (let attempt = 0; attempt < 5; attempt++) {
    try { return JSON.parse((await readFile(path, 'utf8')).replace(/^\uFEFF/, '')); }
    catch (error) {
      if (!(error instanceof SyntaxError) || attempt === 4) throw error;
      await delay(30);
    }
  }
  throw new Error('invalid_json');
}

function canonical(value: unknown): string {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
  if (value !== null && typeof value === 'object') {
    const object = value as Record<string, unknown>;
    return '{' + Object.keys(object).sort().map(key => JSON.stringify(key) + ':' + canonical(object[key])).join(',') + '}';
  }
  return JSON.stringify(value);
}

export class BridgeClient {
  readonly directory: string;
  readonly timeoutMs: number;

  constructor(directory = defaultDataDirectory(), timeoutMs = 5000) {
    this.directory = resolve(directory);
    if (!Number.isFinite(timeoutMs) || timeoutMs < 1 || timeoutMs > 60000) throw new Error('invalid_timeout');
    this.timeoutMs = timeoutMs;
  }

  async status(): Promise<BridgeStatus> {
    let status: BridgeStatus;
    try { status = await readJson(join(this.directory, 'status.json')) as BridgeStatus; }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') throw new Error('bridge_not_started: enable PSAI and restart Probably Stolen');
      throw error;
    }
    if (status?.protocol_version !== '0.1') throw new Error('unsupported_protocol');
    safeId(status.session_id);
    const age = Date.now() - Date.parse(status.updated_at);
    if (!Number.isFinite(age) || age > 15000 || age < -15000 || status.online !== true) throw new Error('bridge_offline');
    return { ...status, guidance: contextualGuidance(await loadGuidance()),
      ...(!status.ready ? { recovery: recoveryFor('game_not_ready') } : {}) };
  }

  async result(id: string, session?: string): Promise<Receipt> {
    const current = await this.status();
    const targetSession = session ?? current.session_id;
    safeId(targetSession); safeId(id);
    let receipt: Receipt;
    try { receipt = await readJson(join(this.directory, `result-${targetSession}-${id}.json`)) as Receipt; }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      return { session_id: targetSession, command_id: id, status: targetSession === current.session_id ? 'pending' : 'uncertain' };
    }
    if (receipt.session_id !== targetSession || receipt.command_id !== id) throw new Error('receipt_identity_mismatch');
    if (!['pending', 'completed', 'error', 'uncertain'].includes(receipt.status)) throw new Error('invalid_receipt');
    if (receipt.status === 'pending' && targetSession !== current.session_id) return { ...receipt, status: 'uncertain' };
    return deliverAttention(receipt);
  }

  async send(tool: Request['tool'], args: Record<string, unknown> = {}, options: {
    revision?: number; commandId?: string; session?: string;
  } = {}): Promise<Receipt> {
    const started = performance.now();
    const finish = (receipt: Receipt): Receipt => {
      const output = deliverAttention(receipt);
      return process.env.PSAI_PROFILE === '1' ? {
        ...output, client_performance: { round_trip_ms: performance.now() - started,
          receipt_bytes: Buffer.byteLength(JSON.stringify(output)) },
      } : output;
    };
    const status = await this.status();
    if (!status.ready) throw new Error('game_not_ready');
    if (options.session && options.session !== status.session_id) throw new Error('stale_session');
    if (tool === 'act' && (!Number.isSafeInteger(options.revision) || options.revision! < 0)) throw new Error('expected_revision_required');
    if (tool === 'act' && !options.session) throw new Error('expected_session_required');
    const request: Request = {
      session_id: status.session_id, command_id: safeId(options.commandId ?? randomUUID()), tool, arguments: args,
      ...(options.revision === undefined ? {} : { expected_revision: options.revision }),
    };
    const receiptPath = join(this.directory, `result-${request.session_id}-${request.command_id}.json`);
    const path = join(this.directory, `request-${request.session_id}-${request.command_id}.json`);
    // An exclusive intent file arbitrates concurrent callers and survives game deletion of request files.
    const intent = join(this.directory, `intent-${request.session_id}-${request.command_id}.json`);
    const body = JSON.stringify(request);
    if (Buffer.byteLength(body) > 65536) throw new Error('request_too_large');
    await mkdir(this.directory, { recursive: true });
    try {
      // Intent is not consumed by the game. Exclusive creation arbitrates callers,
      // and readJson retries another caller observing a write in progress.
      await writeFile(intent, body, { flag: 'wx' });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
      if (canonical(await readJson(intent)) !== canonical(request)) throw new Error('command_id_conflict');
    }
    try { await access(receiptPath); }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      // Republishing the same intent recovers a caller that exited before publication.
      const publish = join(this.directory, `.tmp-${randomUUID()}`);
      await writeFile(publish, body, { flag: 'wx' });
      try {
        for (let attempt = 0; ; attempt++) {
          try { await rename(publish, path); break; }
          catch (error) {
            if (!['EPERM', 'EACCES', 'EBUSY', 'EEXIST'].includes((error as NodeJS.ErrnoException).code ?? '') || attempt >= 4) throw error;
            await delay(30);
          }
        }
      } finally { await unlink(publish).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
    }
    const deadline = Date.now() + this.timeoutMs;
    while (Date.now() < deadline) {
      try {
        await readFile(receiptPath);
        const result = await this.result(request.command_id, request.session_id);
        if (result.request && canonical(result.request) !== canonical(request)) throw new Error('command_id_conflict');
        if (result.status !== 'pending') {
          if (tool === 'observe' && result.status === 'completed') return finish({ ...result, guidance: contextualGuidance(await loadGuidance(), result.data) });
          return finish(result);
        }
      } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
      }
      await delay(100);
    }
    return finish(await this.result(request.command_id, request.session_id));
  }
}
