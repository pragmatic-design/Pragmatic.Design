/**
 * failure-record.mjs — what failed, kept past the terminal it was printed in.
 *
 * ⚠️ The names are not what is missing. `check.mjs` prints each failing test with its message and
 * the pool crowding at the time. Printed ONLY there, they live in a scrollback that has moved on, a
 * pipe through `grep`, a CI log nobody kept — and a failure that does not reproduce is then gone for
 * good: a test that fails on one `--tier full` run of four, its name gone past a filter, leaves only
 * a re-run, which comes back green and proves nothing.
 *
 * ONE FILE PER RED RUN, not one that is overwritten. A flake is a comparison between runs — the same
 * name twice, or two different names — and a single "last failure" file can only answer about the
 * most recent one. The last `KEEP` are retained; a directory that grows without limit is one nobody
 * opens.
 */
import { mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** How many red runs stay on disk. Enough to recognise a flake, few enough to read. */
export const KEEP = 20;

/** Lines of one failure's detail kept in the record. Past any message and its first frames. */
export const DETAIL_LINES = 40;

/**
 * The names of the tests that failed.
 *
 * The xUnit runner prints them regardless of MSBuild verbosity:
 *
 *   [xUnit.net 00:00:03.82]     Some.Namespace.SomeTests.The_Method [FAIL]
 *
 * ⚠️ The MSBuild summary line ("Non superati: 1. Superati: 184.") carries no names and is not matched.
 * An earlier version of this matched a "Non superato Name" shape that `dotnet test` never emits — it
 * was checked against invented strings rather than real output, so it silently found nothing every
 * time. Everything here is driven by `scripts/fixtures/dotnet-test-red-it-IT.txt`, which is a real
 * run's captured output.
 */
export function failedTestNames(out) {
  const names = [];
  for (const line of out.split(/\r?\n/)) {
    const m = line.match(/^\[xUnit\.net[^\]]*\]\s+(\S+)\s+\[FAIL\]/);
    if (m) names.push(m[1]);
  }
  return names;
}

/** How much of the reason the TERMINAL line carries. A terminal line is a line. */
export const TERMINAL_CAP = 160;

/**
 * One capped line per failed test — what the gate prints.
 *
 * ⚠️ It stays capped, and that is deliberate: a failing full run prints
 * up to ten of these, and a wall of frames is how the names stop being read. The detail belongs in the
 * record, which outlives the terminal; {@link failureDetails} is the parallel of this, not its
 * replacement.
 *
 * The reason is NOT on the lines after the `[FAIL]` marker — with several failures those are more
 * `[FAIL]` markers, and the messages arrive together in a summary block at the end. So each name is
 * looked up in that block instead, anchored on the test name and on the `…Exception` suffix, both of
 * which survive translation where the surrounding labels do not.
 */
export function failureLines(out, { cap = TERMINAL_CAP } = {}) {
  const lines = out.split(/\r?\n/);

  return failedTestNames(out).map((name) => {
    // The FIRST such line, not the last: the stack trace names the test too, and anchoring on it
    // lands among the frames where no message follows.
    const at = lines.findIndex((l) =>
      l.includes(name) && !l.startsWith('[xUnit.net') && !/^\s+at\s/.test(l));
    if (at < 0) return name;

    const reason = lines
      .slice(at + 1, at + 5)
      .map((l) => l.trim())
      .find((l) => /\w+(Exception|Error)\s*:/.test(l));

    return reason ? `${name} — ${reason.slice(0, cap)}` : name;
  });
}

/**
 * A line the runner prints for one test's outcome: `  <verdict> <Full.Test.Name> [6 ms]`.
 *
 * ⚠️ Recognised by POSITION and never by the verdict, which is translated — "Non superato" here,
 * "Failed" in CI, two words against one. What makes it this kind of line is the duration in brackets
 * at the end, which is why `[FAIL]` and `Messaggio di errore:` are not mistaken for one.
 *
 * ⚠️ **And the unit is not always `ms`.** A test slower than a second prints `[4 s]`; requiring `ms`
 * would leave it with no outcome line as far as the capture is concerned, and its detail null — on
 * `Pragmatic.SourceGenerator.Tests`, where the generator tests take seconds, most details would come
 * back empty. A fixture taken from a fast suite holds only milliseconds and cannot show it, which is
 * why there is a second fixture whose whole subject is `[4 s]`.
 */
function isOutcomeLine(line) {
  return /\[[^\]]*\d\s*(?:ns|µs|us|ms|s|m|h)\]\s*$/.test(line) && /\.\w/.test(line);
}

/**
 * For each failed test, the runner's own account of it: the message and the stack, verbatim.
 *
 * This is what the terminal cannot keep. `check.mjs` prints one capped line per test on purpose — a
 * failing full run prints up to ten of them, and a wall of frames is how the names stop being read —
 * so the record gets the rest, and a red run that does not reproduce stays diagnosable.
 *
 * ⚠️ **The blocks are interleaved with the passing tests**: there is no blank line after a failure's
 * detail, the next line is
 * `  Superato Some.Other.Test [4 ms]`. So a block runs from its own outcome line to the next outcome
 * line of any test — by position, per {@link isOutcomeLine} — capped at {@link DETAIL_LINES}.
 *
 * ⚠️ Anchored on the outcome line and not on the `[FAIL]` marker: the marker's own block is truncated
 * by the runner (`Stack Trace:` there is abbreviated and carries no `:line`), while the summary block
 * holds the full message and the frames with their file and line.
 */
export function failureDetails(out, { maxLines = DETAIL_LINES } = {}) {
  const lines = out.split(/\r?\n/);

  return failedTestNames(out).map((test) => {
    const at = lines.findIndex((l) => l.includes(test) && isOutcomeLine(l));
    if (at < 0) return { test, detail: null };

    const end = lines.findIndex((l, i) => i > at && isOutcomeLine(l));
    const stop = end < 0 ? lines.length : end;

    const detail = lines.slice(at + 1, Math.min(stop, at + 1 + maxLines))
      .join('\n')
      .replace(/\s+$/, '');

    return { test, detail: detail.length > 0 ? detail : null };
  });
}

const PREFIX = 'failures-';

/** A filename-safe instant. */
function stamp(now) {
  return now.toISOString().replace(/[:.]/g, '-');
}

/**
 * Writes one red run's failures and prunes the old ones.
 *
 * @param {{dir: string, tier: string, only?: string|null, suites: Array<object>, now?: Date}} run
 * @returns {string} the path written
 */
export function record({ dir, tier, only = null, suites, now = new Date() }) {
  mkdirSync(dir, { recursive: true });

  const path = join(dir, `${PREFIX}${stamp(now)}.json`);

  writeFileSync(
    path,
    JSON.stringify({ when: now.toISOString(), tier, only, suites }, null, 2),
  );

  prune(dir);
  return path;
}

/**
 * Leaves the most recent {@link KEEP} records.
 *
 * The names sort chronologically because the stamp is ISO-8601 with its punctuation replaced, which
 * preserves the ordering — a numeric suffix would not, and neither would the file's mtime on a
 * checkout.
 */
export function prune(dir, keep = KEEP) {
  const found = readdirSync(dir)
    .filter((f) => f.startsWith(PREFIX) && f.endsWith('.json'))
    .sort();

  for (const stale of found.slice(0, Math.max(0, found.length - keep)))
    rmSync(join(dir, stale), { force: true });

  return found.length;
}
