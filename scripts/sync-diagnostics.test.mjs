/**
 * The generated diagnostics dictionary.
 *
 * Three things matter: the rows come out in the shape `site-diagnostics.mjs` reads, so the page
 * check has something to compare with; two descriptors with one ID are reported, which no other
 * check finds; and `--check` refuses a document the sources no longer match.
 *
 *   node --test scripts/sync-diagnostics.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), 'sync-diagnostics.mjs');

// The row pattern site-diagnostics.mjs reads (declaredDiagnostics).
const SITE_ROW = /^\| `(PRAG[0-9A-Za-z]+)`[^|]*\| `\w+` \| (\w+) \| ([^|]*)\|/;

/** A throwaway repository with one source file declaring descriptors. */
function repo(descriptors) {
  const root = mkdtempSync(join(tmpdir(), 'sync-diagnostics-'));
  const src = join(root, 'src', 'Diagnostics.cs');
  mkdirSync(dirname(src), { recursive: true });
  mkdirSync(join(root, 'docs'), { recursive: true });
  writeFileSync(src, descriptors, 'utf-8');

  const run = (...args) => spawnSync(process.execPath, [SCRIPT, ...args], {
    env: { ...process.env, PRAGMATIC_DIAGNOSTICS_ROOT: root },
    encoding: 'utf-8',
  });
  const table = () => readFileSync(join(root, 'docs', 'diagnostics.md'), 'utf-8');
  return { root, src, run, table };
}

const descriptor = (symbol, id, title) =>
  `public static readonly DiagnosticDescriptor ${symbol} = new(\n` +
  `    "${id}",\n    "${title}",\n    "{0}",\n    "Cat", DiagnosticSeverity.Warning, true);\n`;

test('every descriptor has a row the site check can read', () => {
  const r = repo(descriptor('Alpha', 'PRAG0201', 'Alpha rule') + descriptor('Beta', 'PRAG0202', 'Beta rule'));
  try {
    assert.equal(r.run().status, 0);
    const rows = r.table().split('\n').map((l) => SITE_ROW.exec(l)).filter(Boolean);
    assert.deepEqual(rows.map((m) => [m[1], m[2], m[3].trim()]),
      [['PRAG0201', 'Warning', 'Alpha rule'], ['PRAG0202', 'Warning', 'Beta rule']]);
  } finally {
    rmSync(r.root, { recursive: true, force: true });
  }
});

test('two descriptors with one ID are reported as a collision', () => {
  const r = repo(descriptor('Alpha', 'PRAG0201', 'Alpha rule') + descriptor('AlsoAlpha', 'PRAG0201', 'Another rule'));
  try {
    const result = r.run();
    assert.equal(result.status, 0);
    assert.match(result.stdout, /collision: PRAG0201/);
    assert.match(r.table(), /\| Collisions \| \*\*1\*\* \|/);
  } finally {
    rmSync(r.root, { recursive: true, force: true });
  }
});

test('--check refuses a document the sources no longer match', () => {
  const r = repo(descriptor('Alpha', 'PRAG0201', 'Alpha rule'));
  try {
    assert.equal(r.run().status, 0);
    assert.equal(r.run('--check').status, 0, 'the document just written matches');

    writeFileSync(r.src, descriptor('Alpha', 'PRAG0201', 'Alpha rule') + descriptor('Beta', 'PRAG0202', 'Beta rule'), 'utf-8');
    const stale = r.run('--check');
    assert.equal(stale.status, 1);
    assert.match(stale.stderr, /does not match the sources/);
  } finally {
    rmSync(r.root, { recursive: true, force: true });
  }
});
