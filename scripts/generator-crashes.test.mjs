/**
 * A generator that throws is reported with the frame it threw from (#566).
 *
 *   node --test scripts/generator-crashes.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { generatorCrashes } from './lib/generator-crashes.mjs';

/**
 * A real error log, captured.
 *
 * ⚠️ Captured and not written: a generator whose `Select` adds a key twice to a `Dictionary`, built with
 * `dotnet build -warnaserror -v q --no-incremental -p:ErrorLog=…`. The console of that build printed one line,
 * `error CS8785: … Eccezione di tipo 'ArgumentException' con messaggio 'An item with the same key has already
 * been added. Key: a'.`, and nothing of where. Trimmed to its CS8785 result (the other one named a local path).
 */
const CRASH = new URL('./fixtures/cs8785-errorlog-it-IT.sarif', import.meta.url);

function dir() {
  return mkdtempSync(join(tmpdir(), 'generator-crashes-'));
}

test('a crash is read with the frame that threw, not only its message', () => {
  const d = dir();
  try {
    copyFileSync(CRASH, join(d, 'App.net10.0.sarif'));

    const [crash, ...rest] = generatorCrashes(d);

    assert.equal(rest.length, 0);
    assert.equal(crash.log, 'App.net10.0.sarif', 'the project the crash was in');
    assert.match(crash.message, /An item with the same key has already been added/);
    assert.match(crash.message, /at Throwing\.ThrowsHere\(\)/, 'the generator method the exception came out of');
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('a log without a crash, and no logs at all, read as no crash', () => {
  const d = dir();
  try {
    writeFileSync(join(d, 'Clean.net10.0.sarif'), JSON.stringify({ version: '1.0.0', runs: [{ results: [{ ruleId: 'CA1050', message: 'x' }] }] }));

    assert.deepEqual(generatorCrashes(d), []);
    assert.deepEqual(generatorCrashes(join(d, 'missing')), []);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('a SARIF 2 message, an object with its text, is read too', () => {
  const d = dir();
  try {
    writeFileSync(join(d, 'V2.net10.0.sarif'), JSON.stringify({ version: '2.1.0', runs: [{ results: [{ ruleId: 'CS8785', message: { text: 'boom\n   at A.B()' } }] }] }));

    assert.match(generatorCrashes(d)[0].message, /at A\.B\(\)/);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('a log that does not parse is a finding, not a silence', () => {
  const d = dir();
  try {
    writeFileSync(join(d, 'Torn.net10.0.sarif'), '{ "runs": [');

    const [crash] = generatorCrashes(d);

    assert.equal(crash.log, 'Torn.net10.0.sarif');
    assert.match(crash.message, /does not parse/);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});
