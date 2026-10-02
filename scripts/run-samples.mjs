#!/usr/bin/env node
/**
 * Runs every sample project and fails when one does not exit 0.
 *
 * The gate's build compiles the samples and does not run them, so without this a sample could compile
 * and throw on its first line for as long as nobody opened it. A sample is the first code a reader copies; one that
 * does not run teaches the reader something that does not work.
 *
 * It runs the assemblies the gate's clean build produced (artifacts/build/bin/<Project>/debug), from the
 * project's own folder as `dotnet run` would. A sample that needs infrastructure the gate does not start,
 * or that is a host serving until stopped, is listed in scripts/samples-excluded.json with the reason —
 * excluded by name, never silently. An exclusion naming a project that no longer exists fails the run:
 * a stale entry would otherwise hide whatever takes that name next.
 *
 *   node scripts/run-samples.mjs [--jobs N] [--timeout SECONDS] [--only substring]
 *
 * --root and --host exist for the script's own cases (run-samples.test.mjs): another tree, and another
 * program than `dotnet` to launch each built sample with.
 */
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i > 0 ? process.argv[i + 1] : fallback;
};
const root = arg('--root', join(dirname(fileURLToPath(import.meta.url)), '..'));
const host = arg('--host', 'dotnet');
const jobs = Number(arg('--jobs', '4'));
const timeoutMs = Number(arg('--timeout', '120')) * 1000;
const only = arg('--only', null);

const excluded = JSON.parse(readFileSync(join(root, 'scripts', 'samples-excluded.json'), 'utf8')).excluded;

const projects = execFileSync('git', ['ls-files', '*samples/*.csproj', '*Samples/*.csproj'], { cwd: root, encoding: 'utf8' })
  .split('\n').map((l) => l.trim()).filter(Boolean);

const results = [];
const known = new Set(projects.map((p) => basename(p, '.csproj')));
for (const [name, reason] of Object.entries(excluded)) {
  if (!known.has(name)) results.push({ name, status: 'fail', detail: 'excluded by samples-excluded.json, but no such sample project exists' });
  else if (!reason || !reason.trim()) results.push({ name, status: 'fail', detail: 'excluded without a reason' });
}

const queue = projects.filter((p) => !only || p.includes(only)).filter((p) => {
  const name = basename(p, '.csproj');
  if (excluded[name] !== undefined) {
    if (excluded[name].trim()) results.push({ name, status: 'excluded', detail: excluded[name] });
    return false;
  }
  return true;
});

async function runOne(project) {
  const name = basename(project, '.csproj');
  const dll = join(root, 'artifacts', 'build', 'bin', name, 'debug', `${name}.dll`);
  if (!existsSync(dll)) return { name, status: 'fail', detail: `not built: ${dll}` };

  const started = Date.now();
  return new Promise((resolve) => {
    const child = spawn(host, [dll], { cwd: join(root, dirname(project)), stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    child.stdout.on('data', (d) => { output += d; });
    child.stderr.on('data', (d) => { output += d; });

    const timer = setTimeout(() => {
      child.kill('SIGKILL');
      resolve({ name, status: 'fail', detail: `did not exit within ${timeoutMs / 1000}s`, output });
    }, timeoutMs);

    child.on('close', (code) => {
      clearTimeout(timer);
      const seconds = ((Date.now() - started) / 1000).toFixed(1);
      resolve(code === 0
        ? { name, status: 'pass', detail: `${seconds}s` }
        : { name, status: 'fail', detail: `exit ${code} after ${seconds}s`, output });
    });
  });
}

async function pool() {
  const running = new Set();
  for (const project of queue) {
    const p = runOne(project).then((r) => { results.push(r); running.delete(p); });
    running.add(p);
    if (running.size >= jobs) await Promise.race(running);
  }
  await Promise.all(running);
}

await pool();

results.sort((a, b) => a.name.localeCompare(b.name));
for (const r of results) {
  const mark = r.status === 'pass' ? 'pass' : r.status === 'excluded' ? 'skip' : 'FAIL';
  console.log(`  ${mark.padEnd(4)}  ${r.name} — ${r.detail}`);
  if (r.status === 'fail' && r.output) {
    // From the exception down: the tail of a long stack would drop the message that says what broke.
    const lines = r.output.trim().split('\n');
    const thrown = lines.findIndex((l) => /Unhandled exception|Error:|Exception:/.test(l));
    const shown = thrown >= 0 ? lines.slice(thrown, thrown + 8) : lines.slice(-8);
    for (const line of shown) console.log(`          ${line.trim()}`);
  }
}

const failed = results.filter((r) => r.status === 'fail');
const passed = results.filter((r) => r.status === 'pass').length;
const skipped = results.filter((r) => r.status === 'excluded').length;
console.log(`samples: ${passed} ran, ${failed.length} failed, ${skipped} excluded by name`);
process.exit(failed.length > 0 ? 1 : 0);
