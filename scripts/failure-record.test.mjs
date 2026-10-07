/**
 * A red run has to survive the terminal it was printed in.
 *
 * ⚠️ The gate prints the failing test names, with their messages and the pool crowding. Printing is
 * not keeping: a test that fails on one `--tier full` run of four, with its name gone past a `grep` on
 * the way to the screen, leaves only a re-run — which comes back green and proves nothing. A flake is
 * a comparison between runs, so one file per red run, not one that is overwritten.
 *
 *   node --test scripts/failure-record.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import {
  record, prune, failedTestNames, failureDetails, failureLines, unnamedFailureTail, KEEP, TERMINAL_CAP,
  UNNAMED_TAIL_LINES,
} from './lib/failure-record.mjs';

function dir() {
  return mkdtempSync(join(tmpdir(), 'failure-record-'));
}

/**
 * A real red run, captured.
 *
 * ⚠️ Captured and not written: this file's own `failureLines` carries the scar of a shape that was
 * invented, checked against other invented strings, and silently matched nothing every time. Two
 * Conformance.Poco tests were broken on purpose — one throwing three frames down, one
 * failing an assertion whose reason runs past the terminal cap — and `dotnet test`'s output was kept
 * exactly as it came, Italian labels and all, because CI does not run in this locale.
 */
const RED = readFileSync(new URL('./fixtures/dotnet-test-red-it-IT.txt', import.meta.url), 'utf-8');

/**
 * A second real capture, whose whole subject is a duration in **seconds**.
 *
 * ⚠️ Trimmed to the window around one failure, because the run it came from is 512 KB — but captured,
 * not written. `Pragmatic.SourceGenerator.Tests` is where this was found: its generator tests take
 * seconds, the runner prints `[4 s]`, and a capture that required `ms` saw no outcome line at all.
 */
const RED_SECONDS = readFileSync(
  new URL('./fixtures/dotnet-test-red-seconds-it-IT.txt', import.meta.url), 'utf-8');

const THREE_FRAMES = 'Conformance.Poco.Tests.Cases.ProbeFailureRecord.AnExceptionFromThreeFramesDown';
const LONG_REASON = 'Conformance.Poco.Tests.Cases.ProbeFailureRecord.AnAssertionWithAReasonOnItsOwnLine';

const detailOf = (test) => failureDetails(RED).find((d) => d.test === test)?.detail;

const suites = [
  { suite: 'Pragmatic.Jobs.Tests', failed: 1, passed: 122, tests: ['Some.Test — a message'] },
];

test('a red run is written, with the tier and the names', () => {
  const d = dir();
  try {
    const path = record({ dir: d, tier: 'full', suites, now: new Date('2026-09-06T10:00:00Z') });
    const written = JSON.parse(readFileSync(path, 'utf-8'));

    assert.equal(written.tier, 'full');
    assert.equal(written.suites[0].suite, 'Pragmatic.Jobs.Tests');
    assert.deepEqual(written.suites[0].tests, ['Some.Test — a message']);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('two red runs are two files: a flake is a comparison, not a latest', () => {
  const d = dir();
  try {
    record({ dir: d, tier: 'full', suites, now: new Date('2026-09-06T10:00:00Z') });
    record({ dir: d, tier: 'full', suites, now: new Date('2026-09-06T10:05:00Z') });

    assert.equal(readdirSync(d).length, 2);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('the oldest are pruned, and the newest survive', () => {
  const d = dir();
  try {
    for (let i = 0; i < KEEP + 5; i++)
      record({
        dir: d,
        tier: 'full',
        suites,
        now: new Date(Date.UTC(2026, 8, 6, 10, i)),
      });

    const left = readdirSync(d).sort();

    assert.equal(left.length, KEEP);
    // The newest is kept, which is the half a "delete the oldest" loop can get backwards.
    assert.ok(left[left.length - 1].includes(`10-${String(KEEP + 4).padStart(2, '0')}`));
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('a directory with no records is left alone', () => {
  const d = dir();
  try {
    assert.equal(prune(d), 0);
    assert.equal(readdirSync(d).length, 0);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

test('files that are not records are not pruned', () => {
  const d = dir();
  try {
    // The gate writes other things under artifacts/. A prune that deleted by count rather than by
    // name would take them with it.
    record({ dir: d, tier: 'full', suites, now: new Date('2026-09-06T10:00:00Z') });
    writeFileSync(join(d, 'sbom.json'), '{}');

    prune(d, 0);

    assert.deepEqual(readdirSync(d), ['sbom.json']);
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});

// ── What the record keeps that the terminal cannot ───────────────────────────────────

test('both failures of the real run are found by name', () => {
  assert.deepEqual(failedTestNames(RED).sort(), [LONG_REASON, THREE_FRAMES].sort());
});

/**
 * ⚠️ The measurement this issue was filed on: six runs of a four-minute container suite produced the
 * same eleven words, and the cause — a `GetProperty` on a JSON property the host omits when null —
 * was one stack frame away.
 */
test('the detail carries the first frame, with its file and its line', () => {
  const detail = detailOf(THREE_FRAMES);

  assert.ok(detail, 'there is a detail at all');
  assert.match(detail, /ProbeFailureRecord\.cs:line 9/,
    'the frame where the exception was actually thrown, three calls below the test method');
  assert.match(detail, /ProbeFailureRecord\.cs:line 16/, 'and the test method itself');
});

/**
 * ⚠️ The other half. The terminal line ends at "160-charac" — mid-word — and the number that the
 * assertion was about (`but found 41`) is past the cap. The workaround a test is otherwise pushed
 * into is putting the datum first and on one line.
 */
test('the detail carries the whole reason, past the terminal cap', () => {
  const line = failureLines(RED).find((l) => l.startsWith(LONG_REASON));
  const detail = detailOf(LONG_REASON);

  assert.ok(!line.includes('but found 41'), 'the terminal line is cut before the number');
  assert.match(detail, /but found 41/, 'and the record has it');
});

/**
 * The control, and it is the whole reason the detail is a second field rather than a longer line: a
 * failing full run prints up to ten of these, and a wall of frames is how the names stop being read.
 */
test('the terminal line is unchanged: one line per test, still capped', () => {
  const lines = failureLines(RED);

  assert.equal(lines.length, 2, 'one per failed test, not one per frame');
  for (const line of lines) {
    assert.ok(!line.includes('\n'), `still one line: ${line}`);
    const reason = line.split(' — ')[1] ?? '';
    assert.ok(reason.length <= TERMINAL_CAP, `still capped at ${TERMINAL_CAP}, saw ${reason.length}`);
  }
});

/**
 * ⚠️ The shape that matters, and the reason a "capture until the next blank line" would keep
 * nothing: the detail blocks are INTERLEAVED with the passing tests. The line after the
 * last frame is `  Superato Conformance.Poco.Tests.Cases.WritingShapes.… [4 ms]`.
 */
test('a block ends at the next test outcome, not at a blank line', () => {
  const detail = detailOf(THREE_FRAMES);

  assert.ok(!/Superato/.test(detail), 'a passing test that follows is not part of the failure');
  assert.ok(!/ProbeFailureRecord\.AnAssertionWithAReasonOnItsOwnLine/.test(detail),
    'and neither is the other failure');
});

/**
 * ⚠️ By position, never by the verdict. "Non superato" here, "Failed" in CI — two words against one —
 * and the labels inside the block ("Messaggio di errore", "Analisi dello stack") are translated too.
 * What identifies an outcome line is the duration in brackets at its end.
 */
test('the capture survives a locale it has never seen', () => {
  const english = RED
    .replace(/Non superato/g, 'Failed')
    .replace(/Superato/g, 'Passed')
    .replace(/Messaggio di errore:/g, 'Error Message:')
    .replace(/Analisi dello stack:/g, 'Stack Trace:');

  const detail = failureDetails(english).find((d) => d.test === THREE_FRAMES)?.detail;

  assert.match(detail ?? '', /ProbeFailureRecord\.cs:line 9/);
});

/**
 * ⚠️ The defect the first fixture could not show, found one story later: a suite whose tests take
 * seconds prints `[4 s]`, and requiring `ms` meant three details of four came back null on
 * `Pragmatic.SourceGenerator.Tests`. A fixture is only evidence about the run it came from.
 */
test('a test measured in seconds has a detail too', () => {
  const [{ test: name, detail }] = failureDetails(RED_SECONDS);

  assert.match(name, /ADottedViaIsResolvedSegmentBySegment$/);
  assert.ok(detail, 'the outcome line reads [4 s], not [4 ms]');
  assert.match(detail, /PRAG0737/, 'the whole assertion message');
  assert.match(detail, /AJoinPathIsCheckedAgainstTheEntityTests\.cs:line 112/, 'and the frame');
});

test('every duration unit the runner prints is an outcome line', () => {
  for (const duration of ['[< 1 ms]', '[413 ms]', '[4 s]', '[1 m]']) {
    const out = `[xUnit.net 00:00:01.00]     A.B.C [FAIL]\n  Failed A.B.C ${duration}\n   Boom\n  Passed A.B.D [1 ms]`;

    assert.match(failureDetails(out)[0].detail ?? '', /Boom/, duration);
  }
});

test('the detail is capped, so one runaway stack cannot be the whole record', () => {
  const [{ detail }] = failureDetails(RED, { maxLines: 2 });

  assert.equal(detail.split('\n').length, 2);
});

test('a green run has no details, and a build failure has no detail to find', () => {
  assert.deepEqual(failureDetails(''), []);
  assert.deepEqual(failureDetails('[xUnit.net 00:00:00.01]     A.B.C [FAIL]'), [{ test: 'A.B.C', detail: null }]);
});

// ── A failure the runner counts but does not name (#118) ─────────────────────────────

/**
 * ⚠️ What happened on 2026-10-07: `Pragmatic.Logging.Tests — 1 failed, 519 passed`, and a record with
 * `"tests": [], "details": []`. Every name comes from a `[FAIL]` line, so a failure reported any other
 * way (a cleanup that throws, an exception after the test returned) left nothing to read, and the
 * rerun was green. This output is written, not captured: the real one was not kept, which is the
 * defect. Its shape is the summary of a run with a failed count and no `[FAIL]` marker.
 */
const COUNTED_NOT_NAMED = [
  '[xUnit.net 00:00:00.05]   Discovering: Pragmatic.Logging.Tests',
  '[xUnit.net 00:00:00.10]   Starting:    Pragmatic.Logging.Tests',
  '[xUnit.net 00:00:02.40]     [Test Class Cleanup Failure (Pragmatic.Logging.Tests.Some.Fixture)]: System.ObjectDisposedException : Cannot access a disposed object.',
  '[xUnit.net 00:00:02.41]   Finished:    Pragmatic.Logging.Tests',
  'Non superato! - Non superati:     1. Superati:   519. Ignorati:     0. Totale:   520.',
].join('\n');

test('a failure counted with no test named keeps the tail of the output', () => {
  assert.deepEqual(failedTestNames(COUNTED_NOT_NAMED), [], 'no [FAIL] line names a test');

  const tail = unnamedFailureTail(COUNTED_NOT_NAMED, 1);

  assert.ok(tail, 'something is kept when the count says red and no name says why');
  assert.match(tail, /Test Class Cleanup Failure/, 'and it is the part that says what failed');
});

test('a named failure keeps no tail: its detail already says why', () => {
  assert.equal(unnamedFailureTail(RED, 2), null);
});

test('a green suite keeps no tail', () => {
  assert.equal(unnamedFailureTail(COUNTED_NOT_NAMED, 0), null);
});

test('the tail is capped, so a long run cannot be the whole record', () => {
  const long = Array.from({ length: UNNAMED_TAIL_LINES * 3 }, (_, i) => `line ${i}`).join('\n');

  const tail = unnamedFailureTail(long, 1);

  assert.equal(tail.split('\n').length, UNNAMED_TAIL_LINES);
  assert.match(tail, new RegExp(`line ${UNNAMED_TAIL_LINES * 3 - 1}$`), 'the end of the run, where the summary is');
});

test('the record holds the detail beside the capped line, and stays a file a person can read', () => {
  const d = dir();
  try {
    const path = record({
      dir: d,
      tier: 'docker',
      suites: [{ suite: 'Conformance.Poco.Tests', failed: 2, passed: 17, tests: failureLines(RED), details: failureDetails(RED) }],
      now: new Date('2026-09-21T19:51:12Z'),
    });
    const written = JSON.parse(readFileSync(path, 'utf-8'));

    assert.equal(written.suites[0].tests.length, 2, 'the terminal lines are still there');
    assert.match(written.suites[0].details.find((x) => x.test === THREE_FRAMES).detail,
      /ProbeFailureRecord\.cs:line 9/);
    assert.ok(statSync(path).size < 64 * 1024,
      'a record nobody opens is a record that does not exist; two capped blocks are kilobytes');
  } finally {
    rmSync(d, { recursive: true, force: true });
  }
});
