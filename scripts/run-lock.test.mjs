/**
 * Two gate runs at once destroy each other, and the wreckage looks like a code failure.
 *
 * ⚠️ Measured: a `--tier docker` started while a `--tier full` is in flight fails with
 * `build --warnaserror … FAIL (390 error markers)`. Everything builds into one `artifacts/build`, so
 * a clean build in one run deletes what the other is compiling. A run that overlapped other test
 * work reported **seven suites not green and 11216 tests against 11753**, with
 * the gate's own "fewer tests than the previous run" warning firing — and every one of those suites
 * was green on its own afterwards.
 *
 * None of that is a flake. It is two processes sharing one output directory, and the cost is not the
 * lost run: it is that a red gate stops meaning "the code is broken".
 *
 *   node --test scripts/run-lock.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { acquire, STALE_AFTER_MS } from './lib/run-lock.mjs';

function dir() {
  return mkdtempSync(join(tmpdir(), 'run-lock-'));
}

test('the first run takes the lock and the second is refused', () => {
  const root = dir();
  try {
    const first = acquire(root);
    assert.ok(first, 'the first run holds the lock');

    const second = acquire(root);
    assert.equal(second, null, 'a second run must be refused, not left to fight over artifacts/build');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('releasing lets the next run in', () => {
  const root = dir();
  try {
    const first = acquire(root);
    first.release();

    const second = acquire(root);
    assert.ok(second, 'the lock is a handle, not a tombstone');
    second.release();
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('releasing twice is harmless', () => {
  const root = dir();
  try {
    const held = acquire(root);
    held.release();
    held.release();

    assert.ok(acquire(root), 'a double release must not leave the lock unusable');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a lock left behind by a killed run goes stale and does not block forever', () => {
  const root = dir();
  try {
    // A run that was killed — Ctrl-C, a crash, a machine restart — leaves the file behind. A gate that
    // then refuses every future run has replaced one unusable state with another.
    const orphan = { pid: 999999, startedAt: new Date(Date.now() - STALE_AFTER_MS - 1000).toISOString() };
    writeFileSync(join(root, 'gate.lock'), JSON.stringify(orphan), 'utf-8');

    assert.ok(acquire(root), 'a lock older than the staleness window is reclaimed');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a fresh lock from another process is respected', () => {
  const root = dir();
  try {
    // ⚠️ A pid that is actually alive. A made-up pid such as 999999 does not express "somebody else",
    // because the lock checks whether the holder exists: a made-up pid is a dead one, and a dead
    // holder is debris rather than a run in progress.
    const live = { pid: process.pid, startedAt: new Date().toISOString() };
    writeFileSync(join(root, 'gate.lock'), JSON.stringify(live), 'utf-8');

    assert.equal(acquire(root), null,
      'the staleness window must not be so eager that it walks over a run that is still going');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a lock whose holder no longer exists is reclaimed at once', () => {
  const root = dir();
  try {
    // Fresh by the clock — seconds old, nowhere near the staleness window — but written by a process
    // that is gone. Killing a gate run leaves exactly this, and without the liveness check every run
    // for the next 45 minutes would be refused over a holder that does not exist.
    const dead = { pid: 999999, startedAt: new Date().toISOString() };
    writeFileSync(join(root, 'gate.lock'), JSON.stringify(dead), 'utf-8');

    const held = acquire(root);
    assert.notEqual(held, null,
      'a lock nobody holds is debris, and waiting out the window for it refuses every run for no reason');
    held?.release();
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the lock names who holds it', () => {
  const root = dir();
  try {
    const held = acquire(root);
    assert.ok(existsSync(join(root, 'gate.lock')));

    // The refusal has to say enough to act on: which process, and since when.
    assert.match(held.describe(), /\d+/, 'the description names the pid');
    held.release();
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
