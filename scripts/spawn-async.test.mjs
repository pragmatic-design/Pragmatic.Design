/**
 * The gate's suite runner has to actually overlap when it is asked to.
 *
 * ⚠️ A pool of N `async` functions each calling `spawnSync` does not overlap: an `async` function
 * that never awaits is a synchronous function, so worker 2 cannot start until worker 1 returns.
 * `--jobs` is then accepted, documented and inert: measured on such a runner, 106 hermetic suites
 * summed to 250.5s inside a 271s tier, and five container suites forced "5 at a time" took the sum
 * rather than the longest.
 *
 * That is invisible from the outside — everything is green, in a plausible order, with a total
 * nobody has a prediction for. So it is measured here, on the two pieces that carry it: a spawn that
 * yields, and a pool that overlaps.
 *
 * ⚠️ **Nothing in this file measures a duration**, and that is a rule rather than a preference. Both
 * pool cases count live workers and assert on the peak; the spawn case has its children wait for each
 * other. A threshold on elapsed time is a statement about how fast the machine is, and goes red for
 * exactly that reason while the runner is working correctly. If a case here ever needs
 * a stopwatch, the question it is asking is the wrong one.
 *
 *   node --test scripts/spawn-async.test.mjs
 */
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { runAsync, runPool } from './lib/spawn-async.mjs';

// The children are files rather than `node -e "…"`: the runner spawns through a shell on Windows —
// as the synchronous one always did, because `dotnet` and `npm` are .cmd shims there — and a shell
// mangles the quotes an inline script needs. A file has no quotes to lose.
let dir;
const script = (name) => join(dir, name);

before(() => {
  dir = mkdtempSync(join(tmpdir(), 'spawn-async-'));
  // ⚠️ The children rendezvous with each other, and that is the whole point of this shape: each one
  // announces itself and then waits until it can see all of its siblings. Overlap is then *forced*
  // rather than inferred — if the runner cannot run them at the same time, they cannot all see each
  // other, and they say so.
  //
  // `argv[2]` is this child's marker, `argv[3]` how many siblings to expect. The 10s bound is a
  // deadlock timeout, not a performance threshold: it fires only when overlap is impossible, so a
  // slow machine makes this case slower and never wrong.
  writeFileSync(script('rendezvous.js'), [
    "const fs = require('fs');",
    "const path = require('path');",
    'const mine = process.argv[2];',
    'const expected = Number(process.argv[3]);',
    'const dir = path.dirname(mine);',
    "const siblings = () => fs.readdirSync(dir).filter((f) => f.startsWith('rv-')).length;",
    "fs.writeFileSync(mine, 'here');",
    'const deadline = Date.now() + 10_000;',
    'const look = () => {',
    '  if (siblings() >= expected) {',
    "    fs.writeFileSync(mine, 'all');",
    '    process.exit(0);',
    '  }',
    '  if (Date.now() > deadline) {',
    "    fs.writeFileSync(mine, 'alone:' + siblings());",
    '    process.exit(0);',
    '  }',
    '  setTimeout(look, 10);',
    '};',
    'look();',
  ].join('\n'), 'utf-8');
  writeFileSync(script('both-streams.js'),
    'console.log("out"); console.error("err");', 'utf-8');
  writeFileSync(script('exit3.js'), 'process.exit(3);', 'utf-8');
  writeFileSync(script('loud.js'),
    'process.stdout.write("x".repeat(2 * 1024 * 1024));', 'utf-8');
});

after(() => rmSync(dir, { recursive: true, force: true }));

test('runAsync yields while the child runs, so three of them overlap', async () => {
  const marks = [1, 2, 3].map((n) => script(`rv-${n}.txt`));

  await Promise.all(marks.map(
    (mark) => runAsync(process.execPath, [script('rendezvous.js'), mark, String(marks.length)])));

  const seen = marks.map((mark) => readFileSync(mark, 'utf-8'));

  // ⚠️ Every child has to have seen every other one. Nothing here is a duration: the assertion is
  // that three processes existed at the same moment, which is what "overlap" means and what a clock
  // can only approximate.
  //
  // `elapsed < 800ms` for three 400ms children is a statement about how fast the machine is: with an
  // installer running it reads 1094ms and goes red, still far short of the ~1200ms a sequential
  // runner would take, while the runner *is* overlapping. ⚠️ Having the children report their own
  // start and end instants and comparing the intervals is better, and still not enough: under a
  // loaded machine the spawn latency alone can exceed the child's lifetime, so three
  // genuinely-parallel children stop overlapping — one run in five red. Making them wait for each
  // other is what takes the clock out of the question entirely.
  assert.deepEqual(seen, ['all', 'all', 'all'],
    'each child waits until it can see all three markers; anything else means they were not alive '
    + 'at the same time, so runAsync did not yield');
});

test('runAsync reports the exit code and both streams', async () => {
  const ok = await runAsync(process.execPath, [script('both-streams.js')]);

  assert.equal(ok.code, 0);
  assert.match(ok.out, /out/);
  assert.match(ok.out, /err/, 'stderr is part of out, as the synchronous runner always did');
  assert.equal(ok.spawnError, null);

  const failed = await runAsync(process.execPath, [script('exit3.js')]);

  assert.equal(failed.code, 3);
});

test('runAsync keeps a spawn failure separate from a non-zero exit', async () => {
  const r = await runAsync('a-command-that-does-not-exist-anywhere', [], { shell: false });

  assert.notEqual(r.code, 0);
  assert.ok(r.spawnError, 'an infrastructure failure must never be readable as a failed command');
});

test('through a shell, a missing command is an exit code and not a spawn error', async () => {
  // Stated rather than assumed: on Windows the runner spawns through cmd.exe, which reports an
  // unknown command by exiting non-zero. The synchronous runner had exactly this property, and a
  // caller that expects `spawnError` to catch a typo would be wrong on the platform the gate runs on.
  const r = await runAsync('a-command-that-does-not-exist-anywhere', [], { shell: true });

  assert.notEqual(r.code, 0);
  assert.equal(r.spawnError, null);
});

test('runAsync survives a child that writes more than a megabyte', async () => {
  // The synchronous runner needed maxBuffer raised past 1 MB: a clean build emits ~1.05 MB of
  // warnings, and the default made spawnSync fail with ENOBUFS — reported as a failed build.
  const r = await runAsync(process.execPath, [script('loud.js')]);

  assert.equal(r.code, 0);
  assert.equal(r.out.length, 2 * 1024 * 1024);
});

test('runPool runs at most `size` at once', async () => {
  let live = 0;
  let peak = 0;

  const work = Array.from({ length: 6 }, () => async () => {
    peak = Math.max(peak, ++live);
    await new Promise((resolve) => setTimeout(resolve, 50));
    live--;
  });

  await runPool(work, 2);

  assert.equal(peak, 2, 'the pool is a bound, not a suggestion');
});

test('runPool of one is sequential, which is what the container tier needs', async () => {
  let peak = 0;
  let live = 0;

  const work = Array.from({ length: 3 }, () => async () => {
    peak = Math.max(peak, ++live);
    await new Promise((resolve) => setTimeout(resolve, 20));
    live--;
  });

  await runPool(work, 1);

  assert.equal(peak, 1,
    'the container suites saturate Docker when they run together, and that has to stay expressible');
});

test('runPool keeps results in the order of the work, not of completion', async () => {
  const work = [300, 50, 150].map((ms, i) => async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
    return i;
  });

  assert.deepEqual(await runPool(work, 3), [0, 1, 2],
    'a report that reorders itself by who finished first is unreadable across runs');
});
