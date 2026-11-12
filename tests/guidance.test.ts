import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { defaultGuidance, loadGuidance } from '../src/client/guidance.ts';

test('default guidance is ordered, advisory, and defers to current user instructions', async () => {
  const guidance = await loadGuidance('');
  assert.equal(guidance.mode, 'advisory');
  assert.equal(guidance.user_instructions_take_precedence, true);
  assert.equal(guidance.version, '10');
  assert.deepEqual(guidance.rules.map(rule => rule.id), ['prompt-response', 'progression-first', 'read-effects', 'window-discipline', 'showcase-purpose', 'rent-and-property', 'discover-operations', 'morning-machines', 'read-daily-report', 'counter-cleanup', 'user-progress-reports', 'trade-context', 'file-documents', 'contact-cards', 'store-idle-items', 'run-machines', 'night-electricity', 'night-outing', 'outing-carry-check', 'upgrade-review', 'accept-events', 'wanted-evidence']);
  assert.deepEqual(guidance.rules, defaultGuidance.rules);
});

test('distributed preferences stay consistent with CLI and MCP defaults', async () => {
  const example = JSON.parse(await readFile(new URL('../psai.preferences.example.json', import.meta.url), 'utf8'));
  assert.deepEqual(example.rules, defaultGuidance.rules);
  assert.equal(example.version, 'local-' + defaultGuidance.version);
});

test('local preferences replace defaults and persist across independent loads', async () => {
  await mkdir(resolve('.cache/tests'), { recursive: true });
  const directory = await mkdtemp(resolve('.cache/tests/preferences-'));
  const path = join(directory, 'preferences.json');
  await writeFile(path, JSON.stringify({ version: 'test-2', rules: [
    { id: 'later', priority: 20, instruction: 'Keep trade items on the tray.' },
    { id: 'first', priority: 1, instruction: 'Store documents.' },
  ] }));
  const first = await loadGuidance(path);
  assert.equal(first.source, 'local');
  assert.equal(first.version, 'test-2');
  assert.deepEqual(first.rules.map(rule => rule.id), ['first', 'later']);
  assert.deepEqual(await loadGuidance(path), first);
});

test('invalid preferences report the problem and retain usable default guidance', async () => {
  await mkdir(resolve('.cache/tests'), { recursive: true });
  const directory = await mkdtemp(resolve('.cache/tests/preferences-invalid-'));
  const path = join(directory, 'preferences.json');
  for (const content of ['{', JSON.stringify({ version: 'bad', rules: [
    { id: 'same', priority: 1, instruction: 'A' }, { id: 'same', priority: 2, instruction: 'B' },
  ] })]) {
    await writeFile(path, content);
    const result = await loadGuidance(path);
    assert.equal(result.error, 'preferences_invalid_or_unreadable');
    assert.equal(result.source, 'default');
    assert.deepEqual(result.rules, defaultGuidance.rules);
  }
  assert.equal((await loadGuidance(join(directory, 'missing.json'))).error, 'preferences_invalid_or_unreadable');
});
