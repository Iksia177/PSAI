import { McpServer } from '@modelcontextprotocol/server';
import { serveStdio } from '@modelcontextprotocol/server/stdio';
import { z } from 'zod';
import { BridgeClient } from './bridge.ts';

const client = new BridgeClient();

function output(value: unknown) {
  return { content: [{ type: 'text' as const, text: JSON.stringify(value) }],
    isError: (value as { status?: string }).status === 'error' };
}

async function call(handler: () => Promise<unknown>) {
  try { return output(await handler()); }
  catch (error) { return output({ status: 'error', error: error instanceof Error ? error.message : String(error) }); }
}

serveStdio(() => {
  const server = new McpServer({ name: 'psai', version: '0.1.0' });
  server.registerTool('psai_status', {
    description: 'Check the local Probably Stolen bridge and session.', inputSchema: {},
    annotations: { readOnlyHint: true },
  }, () => call(() => client.status()));
  for (const tool of ['observe', 'actions'] as const) {
    server.registerTool('psai_' + tool, {
      description: tool === 'observe' ? 'Read current player-visible game state and revision.' : 'List currently available supported actions.',
      inputSchema: {}, annotations: { readOnlyHint: true },
    }, () => call(() => client.send(tool)));
  }
  server.registerTool('psai_inspect', {
    description: 'Query a visible item. Currently supports description.',
    inputSchema: { target: z.string(), detail: z.string().default('description') },
    annotations: { readOnlyHint: true },
  }, args => call(() => client.send('inspect', args)));
  server.registerTool('psai_act', {
    description: 'Invoke one action from the latest observation. move_item needs item/destination, activate_item needs item. Poll or observe after animations. Reuse command_id to query an existing intent.',
    inputSchema: {
      action: z.string(), expected_revision: z.number().int().nonnegative(), session_id: z.string(),
      command_id: z.string().optional(), item: z.string().optional(), destination: z.string().optional(), count: z.number().int().positive().optional(),
    }, annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: false },
  }, args => call(() => {
    const { expected_revision, command_id, session_id, ...action } = args;
    return client.send('act', action, { revision: expected_revision, commandId: command_id, session: session_id });
  }));
  server.registerTool('psai_result', {
    description: 'Query a command receipt, including the original session after a game reload.',
    inputSchema: { command_id: z.string(), session_id: z.string().optional() },
    annotations: { readOnlyHint: true },
  }, args => call(() => client.result(args.command_id, args.session_id)));
  return server;
}, { onerror: error => process.stderr.write(error.message + '\n') });
