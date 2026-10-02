// Cases for sync-skill-examples.mjs: the check fails on every kind of drift, and the sync repairs it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = join(dirname(fileURLToPath(import.meta.url)), 'sync-skill-examples.mjs');
const skill = 'marketplace/plugins/pragmatic-design/skills/pragmatic-use-demo/examples';

function tree() {
  const root = mkdtempSync(join(tmpdir(), 'skill-examples-'));
  const write = (path, text) => {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), text);
  };
  write('app/Orders/PlaceOrder.cs', 'class PlaceOrder {}\n');
  write('scripts/skill-examples.json', JSON.stringify({
    skills: {
      'pragmatic-use-demo': {
        source: 'app',
        testedBy: 'app.tests',
        files: { 'Orders/PlaceOrder.cs': 'A mutation' },
      },
    },
  }));
  return { root, write };
}

const run = (root, ...args) => spawnSync(process.execPath, [script, '--root', root, ...args], { encoding: 'utf8' });

test('the sync writes the copy and the README, and the check then passes', () => {
  const { root } = tree();
  assert.equal(run(root).status, 0);
  assert.equal(readFileSync(join(root, skill, 'Orders/PlaceOrder.cs'), 'utf8'), 'class PlaceOrder {}\n');
  assert.match(readFileSync(join(root, skill, 'README.md'), 'utf8'), /Do not edit here/);
  assert.equal(run(root, '--check').status, 0);
  rmSync(root, { recursive: true });
});

test('a copy edited in place fails the check', () => {
  const { root, write } = tree();
  run(root);
  write(`${skill}/Orders/PlaceOrder.cs`, 'class PlaceOrder { /* edited */ }\n');
  const r = run(root, '--check');
  assert.equal(r.status, 1);
  assert.match(r.stdout, /differs from its source/);
  rmSync(root, { recursive: true });
});

test('a source that changed fails the check until the sync runs', () => {
  const { root, write } = tree();
  run(root);
  write('app/Orders/PlaceOrder.cs', 'class PlaceOrder { int Total; }\n');
  assert.equal(run(root, '--check').status, 1);
  assert.equal(run(root).status, 0);
  assert.equal(run(root, '--check').status, 0);
  rmSync(root, { recursive: true });
});

test('a file the manifest does not name fails the check, and the sync removes it', () => {
  const { root, write } = tree();
  run(root);
  write(`${skill}/Orders/Stale.cs`, 'class Stale {}\n');
  const r = run(root, '--check');
  assert.equal(r.status, 1);
  assert.match(r.stdout, /not in the manifest/);
  run(root);
  assert.equal(existsSync(join(root, skill, 'Orders/Stale.cs')), false);
  rmSync(root, { recursive: true });
});

test('line endings a checkout rewrote are not drift', () => {
  const { root, write } = tree();
  run(root);
  write(`${skill}/Orders/PlaceOrder.cs`, 'class PlaceOrder {}\r\n');
  const readme = readFileSync(join(root, skill, 'README.md'), 'utf8');
  write(`${skill}/README.md`, readme.replace(/\n/g, '\r\n'));
  assert.equal(run(root, '--check').status, 0);
  rmSync(root, { recursive: true });
});

test('several suites are named each in its own code span', () => {
  const { root, write } = tree();
  const manifest = JSON.parse(readFileSync(join(root, 'scripts/skill-examples.json'), 'utf8'));
  manifest.skills['pragmatic-use-demo'].testedBy = ['app.tests', 'other.tests'];
  write('scripts/skill-examples.json', JSON.stringify(manifest));
  assert.equal(run(root).status, 0);
  assert.match(readFileSync(join(root, skill, 'README.md'), 'utf8'), /`app\.tests` and `other\.tests`/);
  rmSync(root, { recursive: true });
});

test('a source that is gone fails both the check and the sync', () => {
  const { root } = tree();
  rmSync(join(root, 'app/Orders/PlaceOrder.cs'));
  assert.equal(run(root, '--check').status, 1);
  assert.equal(run(root).status, 1);
  rmSync(root, { recursive: true });
});
