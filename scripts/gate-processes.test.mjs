/**
 * The gate must not leave build servers behind.
 *
 * ⚠️ MSBuild keeps its worker nodes alive for the next build, and here the next build is minutes or
 * days away. Measured: one `--tier full` from a machine at zero leaves **31 dotnet.exe**, every one
 * an MSBuild node, ~7 GB. Two more runs and the tier took 9m 50s instead of 2m 30s while **four suites
 * executed no tests at all** — 9713 reported instead of 12061, and only the ratchet on a suite that
 * ran no tests after running some kept that from being a green.
 *
 * ⚠️ **What this file does and does not check, stated because the difference matters.** It reads the
 * source of the gate. It does not count processes: a process count is a statement about the whole
 * machine — an IDE, another session, a build somebody started by hand — and a gate case that goes red
 * because of what else is running is the failure mode this guards against. The measurement belongs
 * outside the gate, taken from a machine at zero; what belongs here is the one thing that can
 * silently regress, which is somebody deleting the line.
 *
 *   node --test scripts/gate-processes.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const GATE = 'scripts/check.mjs';
const SETTING = 'MSBUILDDISABLENODEREUSE';

/**
 * The file with its comments blanked out, positions preserved.
 *
 * ⚠️ Written after the first version of this file failed its own control: the assignment was commented
 * out and both cases still passed, because a regex over the raw source matches the text inside a
 * comment. The line is *named* in a comment right above it, twice, so searching the raw source here is
 * guaranteed to find something whatever the code does. Blanked rather than deleted so the offsets the
 * second case compares stay the offsets of the real file.
 */
function code(source) {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, (m) => ' '.repeat(m.length))
    .replace(/(^|\n)([^\n]*?)\/\/[^\n]*/g, (m, head, before) =>
      head + before + ' '.repeat(m.length - head.length - before.length));
}

test('the gate disables MSBuild node reuse', () => {
  const source = code(readFileSync(GATE, 'utf-8'));

  assert.match(source, new RegExp(`process\\.env\\.${SETTING}\\s*=`),
    `${GATE} must disable node reuse, or every run leaves its workers behind`);
});

test('it does so before anything is spawned', () => {
  const source = code(readFileSync(GATE, 'utf-8'));

  const set = source.search(new RegExp(`process\\.env\\.${SETTING}\\s*=`));
  const spawned = Math.min(
    ...['spawnSync(', 'runAsync(', 'execSync(']
      .map((call) => source.indexOf(call, source.indexOf('const SLN')))
      .filter((at) => at >= 0));

  // ⚠️ `set >= 0` first, and it is not belt-and-braces: `search` answers -1 when there is no match,
  // and -1 is less than every offset, so an ordering assertion alone passes loudest exactly when the
  // assignment has been deleted. The first version of this case did that.
  assert.ok(set >= 0, `${SETTING} is not set in ${GATE} at all`);

  // The ordering is the half that could go wrong quietly: a child that starts before the variable is
  // set inherits the default, and the run looks exactly the same either way.
  assert.ok(set < spawned,
    `${SETTING} is set at ${set} and the first child is spawned at ${spawned} — it has to come first`);
});
