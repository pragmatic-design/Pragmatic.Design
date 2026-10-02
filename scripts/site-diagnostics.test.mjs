/**
 * The published diagnostics reference against the generated dictionary.
 *
 * The cases worth a test are the ones where "documented" and "mentioned" come apart: the page names
 * IDs in its prose and in its range table, and a check that counted those would call a diagnostic
 * documented because some other row happens to cite it. The other one is the suppressors, which have
 * rows and no descriptor: the check must stay one-way or it would demand descriptors that cannot
 * exist.
 *
 *   node --test scripts/site-diagnostics.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), 'site-diagnostics.mjs');

/** A throwaway tree: a generated dictionary and the page that is supposed to document it. */
function run(dictionaryRows, pageBody) {
  const root = mkdtempSync(join(tmpdir(), 'site-diagnostics-'));

  const dictionary = join(root, 'docs', 'diagnostics.md');
  mkdirSync(dirname(dictionary), { recursive: true });
  if (dictionaryRows !== null)
    writeFileSync(dictionary,
      '### Validation — 2 in use · range `PRAG0200`–`PRAG0299`\n\n' +
      '| ID | Symbol | Severity | Title |\n|---|---|---|---|\n' +
      dictionaryRows, 'utf-8');

  const page = join(root, 'site', 'docs', 'src', 'content', 'docs', 'reference', 'diagnostics.md');
  mkdirSync(dirname(page), { recursive: true });
  writeFileSync(page, pageBody, 'utf-8');

  const result = spawnSync(process.execPath, [SCRIPT, '--check'], {
    env: { ...process.env, PRAGMATIC_SITE_DIAGNOSTICS_ROOT: root },
    encoding: 'utf-8',
  });

  rmSync(root, { recursive: true, force: true });
  return result;
}

const declared = (id, title) => `| \`${id}\` | \`Symbol${id}\` | Error | ${title} |\n`;
const row = (id, meaning) => `| \`${id}\` | Error | ${meaning} |\n`;

test('a page with a row per declared diagnostic passes', () => {
  const result = run(
    declared('PRAG0201', 'Alpha') + declared('PRAG0202', 'Beta'),
    row('PRAG0201', 'Do this.') + row('PRAG0202', 'Do that.'));

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /2 declared diagnostics/);
});

test('a declared diagnostic with no row fails, and is named', () => {
  const result = run(
    declared('PRAG0201', 'Alpha') + declared('PRAG0202', 'Beta'),
    row('PRAG0201', 'Do this.'));

  assert.equal(result.status, 1);
  assert.match(result.stderr, /PRAG0202 — Error — Beta/);
  assert.doesNotMatch(result.stderr, /PRAG0201/, 'the ID that has a row is not reported');
});

test('a mention outside a row does not count as documentation', () => {
  const mentions =
    '| `PRAG0200-0299` | Validation | [`Features/Validation/`](…) |\n' +
    '| `PRAG0201` | Error | Do this, unlike `PRAG0202`. |\n' +
    'The text also names `PRAG0202` in a paragraph.\n';

  const result = run(declared('PRAG0201', 'Alpha') + declared('PRAG0202', 'Beta'), mentions);

  assert.equal(result.status, 1, 'a cited ID is not a documented ID');
  assert.match(result.stderr, /PRAG0202/);
});

test('a row the dictionary has no descriptor for is allowed', () => {
  const result = run(declared('PRAG0201', 'Alpha'),
    row('PRAG0201', 'Do this.') + '| `PRAGS001` | — | A suppressor, which is not a descriptor. |\n');

  assert.equal(result.status, 0, result.stderr);
});

test('a missing dictionary fails, rather than comparing nothing with the page', () => {
  const result = run(null, row('PRAG0201', 'Do this.'));

  assert.equal(result.status, 1);
  assert.match(result.stderr, /the dictionary does not exist/);
});

test('a dictionary whose rows cannot be read fails, rather than declaring nothing', () => {
  const result = run('| PRAG0201 | Alpha |\n', row('PRAG0201', 'Do this.'));

  assert.equal(result.status, 1);
  assert.match(result.stderr, /no diagnostic read/);
});

test('a dictionary the page has not caught up with names every missing ID', () => {
  const result = run(
    declared('PRAG0201', 'Alpha') + declared('PRAG0202', 'Beta') + declared('PRAG0203', 'Gamma'),
    row('PRAG0201', 'Do this.'));

  assert.equal(result.status, 1);
  assert.match(result.stderr, /2 of 3 declared diagnostics/);
});
