/**
 * One gate run at a time, per repository.
 *
 * ⚠️ Everything the gate builds goes into a single `artifacts/build`. Two runs at once therefore
 * delete each other's output mid-compile, and what comes out looks like a code failure: measured,
 * a `--tier docker` started beside a `--tier full` reports
 * `build --warnaserror … FAIL (390 error markers)`, and another overlapping run reported seven
 * suites not green and 11216 tests against 11753 — every one of them green on its own afterwards.
 *
 * The cost of that is not the lost run. It is that a red gate stops meaning "the code is broken",
 * which is the only thing this script is for. So the second run is **refused, with a reason**, rather
 * than left to produce a plausible-looking wreck.
 */
import { writeFileSync, readFileSync, existsSync, mkdirSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';

/**
 * How long a lock file is believed before it is treated as debris.
 *
 * ⚠️ A gate that refuses forever because a run was once killed with Ctrl-C has replaced one unusable
 * state with another. The window is generous — well past the slowest tier — because reclaiming a lock
 * that is still held is the worse mistake of the two: it puts the two runs back in the same
 * directory, which is the thing this exists to prevent.
 */
export const STALE_AFTER_MS = 45 * 60 * 1000;

const LOCK_NAME = 'gate.lock';

/**
 * Takes the run lock, or returns `null` when another run holds it.
 *
 * The handle carries `release()` — idempotent, because it is called from a `finally` that may run
 * after an earlier explicit release — and `describe()`, which says who holds it and since when, so a
 * refusal is something the reader can act on rather than a wall.
 */
export function acquire(dir) {
  const path = join(dir, LOCK_NAME);

  const held = read(path);
  if (held && !isStale(held) && !holderIsGone(held.pid))
    return null;

  mkdirSync(dirname(path), { recursive: true });
  const mine = { pid: process.pid, startedAt: new Date().toISOString() };
  writeFileSync(path, JSON.stringify(mine), 'utf-8');

  let released = false;
  return {
    describe: () => describe(mine),
    release() {
      if (released) return;
      released = true;
      // Only ever removes a lock: never touches build output, so a release racing another run's
      // acquire costs at worst one refused run, not a corrupted one.
      rmSync(path, { force: true });
    },
  };
}

/** What the holder of an existing lock looks like, for the refusal message. */
export function describeHolder(dir) {
  const held = read(join(dir, LOCK_NAME));
  return held ? describe(held) : null;
}

function describe(lock) {
  const since = Date.parse(lock.startedAt);
  const minutes = Number.isFinite(since) ? Math.round((Date.now() - since) / 60000) : null;
  return minutes === null
    ? `pid ${lock.pid}`
    : `pid ${lock.pid}, started ${minutes} min ago`;
}

function read(path) {
  if (!existsSync(path)) return null;
  try {
    const lock = JSON.parse(readFileSync(path, 'utf-8'));
    return typeof lock?.pid === 'number' ? lock : null;
  } catch {
    // An unreadable lock is debris, not a holder: a run killed mid-write leaves a truncated file, and
    // refusing every future run over it would be the failure this guards against, inverted.
    return null;
  }
}

function isStale(lock) {
  const since = Date.parse(lock.startedAt);
  return !Number.isFinite(since) || Date.now() - since > STALE_AFTER_MS;
}

/**
 * Whether the process that wrote the lock is gone. A lock nobody holds is debris, exactly like the
 * truncated file above, and waiting out the staleness window for it is 45 minutes of a gate that
 * refuses every run for no reason — which is what a killed run otherwise produces.
 *
 * ⚠️ Only ESRCH counts as gone. EPERM means the pid exists and belongs to somebody else, and a
 * recycled pid reads as alive: both leave the lock held, which is the safe direction. Reclaiming a
 * lock that is still held is the mistake this file exists to prevent, and it stays impossible here.
 */
function holderIsGone(pid) {
  if (!Number.isFinite(pid) || pid <= 0) return true;
  try {
    process.kill(pid, 0);
    return false;
  } catch (err) {
    return err?.code === 'ESRCH';
  }
}
