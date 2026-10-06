/**
 * The benchmark ratchet and the A/B report, on report files written here.
 *
 * ⚠️ What this covers and what it does not. It proves the ratchet refuses one more allocation and a
 * benchmark with no baseline, lets the averaging noise through, and asks for a lower baseline; and that
 * the A/B report pairs the same benchmark on both sides and reads BenchmarkDotNet's time cells. It runs
 * no benchmark: that the workflow runs them, and that the ratchet goes red on a real allocation, is
 * proven by the workflow run (#55).
 *
 *   node --test scripts/benchmarks.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import {
  TOLERANCE_BYTES,
  abReport,
  allocationsFromJson,
  compareAllocations,
  parseTime,
  rowsFromCsv,
  timesFromCsv,
} from './benchmarks.mjs';

test('one more allocation fails the ratchet', () => {
  const result = compareAllocations({ 'A.B.Log': 2368 + 24 }, { 'A.B.Log': 2368 });

  assert.equal(result.failed, true);
  assert.deepEqual(result.regressions, [{ name: 'A.B.Log', bytes: 2392, base: 2368 }]);
});

test('the averaging noise of a many-operation benchmark passes', () => {
  const result = compareAllocations({ 'A.B.HighVolume': 591 + TOLERANCE_BYTES }, { 'A.B.HighVolume': 591 });

  assert.equal(result.failed, false);
  assert.deepEqual(result.regressions, []);
});

test('a benchmark with no baseline fails, so a new one arrives with its baseline', () => {
  const result = compareAllocations({ 'A.B.New': 100 }, {});

  assert.equal(result.failed, true);
  assert.deepEqual(result.unbaselined, [{ name: 'A.B.New', bytes: 100 }]);
});

test('allocating less passes and asks for the baseline to come down', () => {
  const result = compareAllocations({ 'A.B.Log': 2000 }, { 'A.B.Log': 2368 });

  assert.equal(result.failed, false);
  assert.deepEqual(result.improvements, [{ name: 'A.B.Log', bytes: 2000, base: 2368 }]);
});

// BenchmarkDotNet exits 0 when it cannot build a benchmark (seen on the first run of the workflow, #55):
// a benchmark that failed to run is only visible as one missing from the measurements.
test('a baselined benchmark that did not run fails, so a broken benchmark cannot pass by measuring nothing', () => {
  const result = compareAllocations({ 'A.B.Log': 2368 }, { 'A.B.Log': 2368, 'A.B.Gone': 10 });

  assert.equal(result.failed, true);
  assert.deepEqual(result.missing, [{ name: 'A.B.Gone', base: 10 }]);
});

test('allocations are read from the JSON reports, the larger of two jobs kept', () => {
  const dir = mkdtempSync(join(tmpdir(), 'bench-json-'));
  mkdirSync(join(dir, 'results'));
  const report = {
    Benchmarks: [
      { FullName: 'A.B.Log', Memory: { BytesAllocatedPerOperation: 100 } },
      { FullName: 'A.B.Log', Memory: { BytesAllocatedPerOperation: 120 } },
      { FullName: 'A.B.NoMemory' },
    ],
  };
  writeFileSync(join(dir, 'results', 'A.B-report-full-compressed.json'), JSON.stringify(report));

  assert.deepEqual(allocationsFromJson(dir), { 'A.B.Log': 120 });
});

test('BenchmarkDotNet time cells are read in nanoseconds', () => {
  assert.equal(parseTime('559.2 μs'), 559_200);
  assert.equal(parseTime('1,093.9 μs'), 1_093_900);
  assert.equal(parseTime('10.595 ms'), 10_595_000);
  assert.equal(parseTime('62.86 ns'), 62.86);
  assert.equal(parseTime('NA'), null);
});

test('a CSV row keeps its parameters, so two rows of one method stay apart', () => {
  const csv = [
    'Method;Job;IterationCount;Size;Mean;Error',
    'Write;DefaultJob;Default;10;1.5 μs;0.1 μs',
    'Write;DefaultJob;Default;100;12.0 μs;0.4 μs',
  ].join('\n');

  assert.deepEqual(rowsFromCsv(csv), [
    { method: 'Write(Size=10)', job: 'DefaultJob', meanNs: 1500 },
    { method: 'Write(Size=100)', job: 'DefaultJob', meanNs: 12000 },
  ]);
});

test('the A/B report pairs a benchmark with itself across the two sides', () => {
  const write = (root, mean) => {
    mkdirSync(join(root, 'results'), { recursive: true });
    writeFileSync(join(root, 'results', 'A.B-report.csv'), `Method;Job;Mean\nLog;DefaultJob;${mean}\n`);
  };
  const base = mkdtempSync(join(tmpdir(), 'bench-base-'));
  const head = mkdtempSync(join(tmpdir(), 'bench-head-'));
  write(base, '200.0 ns');
  write(head, '150.0 ns');

  const report = abReport(timesFromCsv(base), timesFromCsv(head));

  assert.match(report, /\| A\.B \| Log \| DefaultJob \| 200\.0 ns \| 150\.0 ns \| 0\.75 \|/);
  assert.match(report, /Geometric mean of the ratios over 1 benchmark\(s\): \*\*0\.750\*\*/);
});
