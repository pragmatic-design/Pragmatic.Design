/**
 * Running child processes without blocking the event loop, and a bounded pool over them.
 *
 * ⚠️ A pool of N `async` workers over `spawnSync` is not parallel: an `async` function that never
 * awaits is a synchronous function, so the second worker cannot start until the first returns.
 * `--jobs` would be accepted and inert, and the tier's total would be the **sum** of its suites,
 * never a makespan. That is why the gate's suite runner spawns through this.
 *
 * Two pieces, separated because they fail differently: a spawn that yields, and a pool that bounds.
 * Both are measured in `scripts/spawn-async.test.mjs`, which asks the sum-versus-max question
 * directly rather than inferring it from a wall clock.
 */
import { spawn } from 'node:child_process';

/**
 * Quotes a value a shell would otherwise split on whitespace.
 *
 * ⚠️ Only when a shell is in play, and only when it is needed. `spawn` with `shell: true` hands the
 * command line to cmd.exe verbatim, so an executable or a path containing a space — Node's own
 * `process.execPath` under Program Files, for one — is split into two words, and the call fails with
 * an exit code the caller reads as "the command failed". The synchronous runner had the same hole
 * and never fell in it because every command the gate spawns is a bare name on PATH.
 */
function quoteForShell(value, shell) {
  if (!shell || typeof value !== 'string') return value;
  return /\s/.test(value) && !value.startsWith('"') ? `"${value}"` : value;
}

/**
 * Runs a command and captures its output, resolving when the child exits.
 *
 * The contract is the synchronous runner's, deliberately: `{ code, out, spawnError }`, with stderr
 * appended to stdout and an infrastructure failure kept separate from the exit code — reading a
 * spawn error as a non-zero exit is how a perfectly good build was once reported as FAIL.
 *
 * ⚠️ No `maxBuffer`. The synchronous runner had to raise it past 1 MB because a clean build emits
 * ~1.05 MB of warnings and the default made `spawnSync` fail with ENOBUFS; here the chunks are
 * collected as they arrive, so the only bound is memory. That is the right trade for a gate whose
 * loudest output is the thing it exists to report.
 *
 * ⚠️ Through a shell, an unknown command is an **exit code**, not a spawn error: cmd.exe reports it
 * itself. `spawnError` catches what the shell never sees.
 */
export function runAsync(cmd, cmdArgs, { shell = process.platform === 'win32' } = {}) {
  return new Promise((resolve) => {
    let child;

    try {
      child = spawn(
        quoteForShell(cmd, shell),
        cmdArgs.map((a) => quoteForShell(a, shell)),
        { shell, windowsHide: true });
    } catch (error) {
      resolve({ code: -1, out: '', spawnError: String(error) });
      return;
    }

    const chunks = [];
    let spawnError = null;

    child.stdout?.on('data', (d) => chunks.push(d));
    child.stderr?.on('data', (d) => chunks.push(d));

    // 'error' can fire instead of, or before, 'close'. Recorded rather than resolved here, so that
    // the handler below is the single place that decides what the caller is told.
    child.on('error', (error) => {
      spawnError = String(error);
      if (child.exitCode === null && !child.connected)
        resolve({ code: -1, out: Buffer.concat(chunks).toString('utf8'), spawnError });
    });

    child.on('close', (code) => resolve({
      code: code ?? (spawnError ? -1 : 1),
      out: Buffer.concat(chunks).toString('utf8'),
      spawnError,
    }));
  });
}

/**
 * Runs the given thunks with at most `size` in flight, and answers in the order they were given.
 *
 * ⚠️ The order matters as much as the bound. A report that reorders itself by who finished first
 * cannot be compared against yesterday's, and comparing runs is most of what a gate's output is for.
 */
export async function runPool(work, size) {
  const results = new Array(work.length);
  let next = 0;

  const worker = async () => {
    while (true) {
      const index = next++;
      if (index >= work.length) return;
      results[index] = await work[index]();
    }
  };

  await Promise.all(
    Array.from({ length: Math.max(1, Math.min(size, work.length)) }, worker));

  return results;
}
