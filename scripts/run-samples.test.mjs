// Cases for run-samples.mjs: a sample that throws, hangs or was never built fails the run; an exclusion
// needs a reason and a project to exclude.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = join(dirname(fileURLToPath(import.meta.url)), 'run-samples.mjs');

// A sample is a tracked csproj under a samples/ folder; its "built assembly" is a script node runs
// in place of dotnet (--host), so each case decides how the sample exits.
function tree(samples, excluded = {}) {
  const root = mkdtempSync(join(tmpdir(), 'run-samples-'));
  const write = (path, text) => {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), text);
  };
  write('scripts/samples-excluded.json', JSON.stringify({ excluded }));
  for (const [name, body] of Object.entries(samples)) {
    write(`Mod/samples/${name}/${name}.csproj`, '<Project />\n');
    if (body !== null) write(`artifacts/build/bin/${name}/debug/${name}.dll`, body);
  }
  spawnSync('git', ['init', '-q'], { cwd: root });
  spawnSync('git', ['add', '-A'], { cwd: root });
  return root;
}

const run = (root, ...args) => spawnSync(
  process.execPath, [script, '--root', root, '--host', process.execPath, '--timeout', '3', ...args],
  { encoding: 'utf8' });

test('samples that exit 0 pass', () => {
  const root = tree({ 'A.Samples': 'process.exit(0)', 'B.Samples': 'console.log("ok")' });
  const r = run(root);
  assert.equal(r.status, 0, r.stdout);
  assert.match(r.stdout, /2 ran, 0 failed/);
  rmSync(root, { recursive: true, force: true });
});

test('a sample that throws fails the run and shows its output', () => {
  const root = tree({ 'A.Samples': 'throw new Error("first line")' });
  const r = run(root);
  assert.equal(r.status, 1);
  assert.match(r.stdout, /FAIL\s+A\.Samples/);
  assert.match(r.stdout, /first line/);
  rmSync(root, { recursive: true, force: true });
});

test('a sample that never exits fails on the timeout', () => {
  const root = tree({ 'A.Samples': 'setInterval(() => {}, 1000)' });
  const r = run(root);
  assert.equal(r.status, 1);
  assert.match(r.stdout, /did not exit within 3s/);
  rmSync(root, { recursive: true, force: true });
});

test('a sample the build did not produce fails', () => {
  const root = tree({ 'A.Samples': null });
  const r = run(root);
  assert.equal(r.status, 1);
  assert.match(r.stdout, /not built/);
  rmSync(root, { recursive: true, force: true });
});

test('an excluded sample is not run and is named with its reason', () => {
  const root = tree({ 'Web.Samples': 'setInterval(() => {}, 1000)' }, { 'Web.Samples': 'A web host.' });
  const r = run(root);
  assert.equal(r.status, 0, r.stdout);
  assert.match(r.stdout, /skip\s+Web\.Samples — A web host\./);
  rmSync(root, { recursive: true, force: true });
});

test('an exclusion without a reason, or for a project that does not exist, fails', () => {
  const root = tree({ 'A.Samples': 'process.exit(0)' }, { 'A.Samples': ' ', 'Gone.Samples': 'Was a web host.' });
  const r = run(root);
  assert.equal(r.status, 1);
  assert.match(r.stdout, /A\.Samples — excluded without a reason/);
  assert.match(r.stdout, /Gone\.Samples — .*no such sample project exists/);
  rmSync(root, { recursive: true, force: true });
});
