import { parseArgs } from 'node:util';
import { BridgeClient, bridgeErrorOutput } from './bridge.ts';

try {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      'data-dir': { type: 'string' }, timeout: { type: 'string' },
      revision: { type: 'string' }, id: { type: 'string' }, session: { type: 'string' },
    },
  });
  const client = new BridgeClient(values['data-dir'], values.timeout === undefined ? 5000 : Number(values.timeout));
  const [command, argument] = positionals;
  let result: unknown;
  if (command === 'status') result = await client.status();
  else if (command === 'result') {
    if (!argument) throw new Error('result_requires_command_id');
    result = await client.result(argument, values.session);
  } else if (command === 'observe' || command === 'actions' || command === 'inspect' || command === 'act') {
    const args = argument === undefined ? {} : JSON.parse(argument);
    if (args === null || Array.isArray(args) || typeof args !== 'object') throw new Error('arguments_must_be_object');
    result = await client.send(command, args, {
      revision: values.revision === undefined ? undefined : Number(values.revision),
      commandId: values.id, session: values.session,
    });
  } else throw new Error('Usage: psai status|observe|actions|inspect <json>|act <json> --revision N|result <id> [--session ID] [--data-dir PATH]');
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
  if ((result as { status?: string }).status === 'error') process.exitCode = 1;
} catch (error) {
  process.stdout.write(JSON.stringify(bridgeErrorOutput(error)) + '\n');
  process.exitCode = 1;
}
