/**
 * The gate runs against the repository it lives in, from whatever directory it is started.
 *
 * Started from another directory it used to read part of the repository from there: it found the test
 * projects of whatever tree sat under that directory, and then crashed on `Pragmatic.Design.slnx` with Node's
 * stack trace (#302). `--tier verify` is the tier run here because it touches nothing and is cheap, and it goes
 * through the same discovery every tier does.
 *
 *   node --test scripts/check-root.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = join(dirname(fileURLToPath(import.meta.url)), 'check.mjs');
const root = join(dirname(fileURLToPath(import.meta.url)), '..');

const verify = (cwd) => spawnSync(process.execPath, [script, '--tier', 'verify'], { cwd, encoding: 'utf8' });
const projectCount = (output) => output.match(/(\d+) test projects/)?.[1];

test('started from another directory, the gate reads the repository it lives in', () => {
  // A tree with a test project of its own, which the gate must not pick up.
  const elsewhere = mkdtempSync(join(tmpdir(), 'check-root-'));
  mkdirSync(join(elsewhere, 'Other', 'tests'), { recursive: true });
  writeFileSync(join(elsewhere, 'Other', 'tests', 'Other.Tests.csproj'), '<Project />');

  const fromRoot = verify(root);
  const fromElsewhere = verify(elsewhere);

  assert.equal(fromElsewhere.status, fromRoot.status, fromElsewhere.stderr);
  assert.doesNotMatch(fromElsewhere.stderr, /ENOENT/);
  assert.equal(projectCount(fromElsewhere.stdout), projectCount(fromRoot.stdout));
});
