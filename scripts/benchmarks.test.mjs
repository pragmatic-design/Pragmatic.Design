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
  allocationReport,
  allocationsFromJson,
  compareAllocations,
  operationsFromJson,
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

// Three runner runs of the endpoint benchmarks moved by up to 270 B on a 483 KB document (0.056%):
// buffer growth averaged over a handful of operations, not a code change (#55).
test('a large benchmark moves by its run-to-run drift without failing, and fails on sixty allocations', () => {
  const baseline = { 'A.B.CitmCatalog': 482_837 };

  assert.equal(compareAllocations({ 'A.B.CitmCatalog': 482_837 + 270 }, baseline).failed, false);
  assert.equal(compareAllocations({ 'A.B.CitmCatalog': 482_837 + 60 * 24 }, baseline).failed, true);
});

// The blocking concurrent row on the runner, eight runs after #136: 134,841 to 145,159 B on a baseline
// of 134,859 (#227). Its direct twin, the same harness without the blocking wait, measured 27,832 B in
// all eight, and keeps the tolerance every other benchmark has.
test('a row whose allocations follow the scheduler moves by its measured spread, and no further', () => {
  const blocking = 'Pragmatic.Endpoints.Benchmarks.Authorization.ScopeResolutionBenchmarks.Scopes_Blocking_ConcurrentRequests';
  const direct = 'Pragmatic.Endpoints.Benchmarks.Authorization.ScopeResolutionBenchmarks.Scopes_Direct_ConcurrentRequests';
  const baseline = { [blocking]: 134_859, [direct]: 27_832 };

  assert.equal(compareAllocations({ [blocking]: 145_159, [direct]: 27_832 }, baseline).failed, false);
  assert.equal(compareAllocations({ [blocking]: 134_859 * 1.08 + 1, [direct]: 27_832 }, baseline).failed, true);
  assert.equal(compareAllocations({ [blocking]: 134_859, [direct]: 27_832 + 24 * 4 }, baseline).failed, true);
});

// The host's row on canada.json, on the runner: 1,218 B over 16 operations, 1,156 to 1,165 over 32, 1,134
// to 1,139 over 64, the same code every time (#298). A fixed amount per measurement, divided by however
// many operations BenchmarkDotNet chose for that runner's speed.
test('a one-off allocation per measurement is allowed in proportion to the operations it was averaged over', () => {
  const host = 'Pragmatic.Endpoints.Benchmarks.ResponseSerializationBenchmarks_Canada.Root_.Host_Reflection';
  const plain = 'A.B.SameNumbers';
  const baseline = { [host]: 1134 };

  assert.equal(compareAllocations({ [host]: 1218 }, baseline, null, null, { [host]: 16 }).failed, false);
  assert.equal(compareAllocations({ [host]: 1165 }, baseline, null, null, { [host]: 32 }).failed, false);
  assert.equal(compareAllocations({ [host]: 1134 + 24 }, baseline, null, null, { [host]: 1024 }).failed, true);
  assert.equal(compareAllocations({ [plain]: 1218 }, { [plain]: 1134 }, null, null, { [plain]: 16 }).failed, true);
});

// The constant search on the runner, nineteen samples over nine runs: 113,875 to 114,623 B, at 256 and at
// 512 operations alike (#298). Each operation compiles a query into EF Core's cache, and what that costs
// moves with the cache, not with the code.
test('a row whose allocations follow a cache moves by its measured spread, and no further', () => {
  const constant = 'Pragmatic.Persistence.Benchmarks.GridFilterBenchmarks.Search_ValueAsConstant';
  const baseline = { [constant]: 113_875 };

  assert.equal(compareAllocations({ [constant]: 114_623 }, baseline).failed, false);
  assert.equal(compareAllocations({ [constant]: 113_875 * 1.007 + 1 }, baseline).failed, true);
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

// A pull request runs only the classes its change reaches (#172): the baseline of the others is not
// missing, nothing was asked of it.
test('on a partial run only the classes that ran are judged against the baseline', () => {
  const baseline = { 'A.B.Log': 2368, 'A.B.Gone': 10, 'A.Other.Write': 50 };

  const result = compareAllocations({ 'A.B.Log': 2368 }, baseline, ['A.B']);

  assert.deepEqual(result.missing, [{ name: 'A.B.Gone', base: 10 }]);
  assert.equal(result.failed, true);
  assert.equal(compareAllocations({ 'A.B.Log': 2368, 'A.B.Gone': 10 }, baseline, ['A.B']).failed, false);
});

// A new class has no baseline entry to go missing from: if it builds nothing, only this sees it.
test('on a partial run a selected class that measured nothing fails', () => {
  const result = compareAllocations({ 'A.B.Log': 2368 }, { 'A.B.Log': 2368 }, ['A.B', 'A.New']);

  assert.equal(result.failed, true);
  assert.deepEqual(result.notRun, [{ name: 'A.New' }]);
});

// BenchmarkDotNet skips a class whose [GlobalSetup] throws and exits 0; a class that never ran has no
// baseline entry to go missing from. RedactionOverheadBenchmarks went unmeasured every night that way (#183).
test('on a full run a listed class that measured nothing fails and is named, and the baseline is still judged whole', () => {
  const baseline = { 'A.B.Log': 2368, 'A.Other.Write': 50, 'A.Gone.Write': 10 };

  const result = compareAllocations({ 'A.B.Log': 2368, 'A.Other.Write': 50 }, baseline, null, ['A.B', 'A.Other', 'A.Skipped']);

  assert.equal(result.failed, true);
  assert.deepEqual(result.notRun, [{ name: 'A.Skipped' }]);
  assert.deepEqual(result.missing, [{ name: 'A.Gone.Write', base: 10 }]);
  assert.match(allocationReport(result, 2), /listed class\(es\) measured nothing[\s\S]*`A\.Skipped`/);
});

test('on a full run every listed class that ran within its baseline passes', () => {
  const baseline = { 'A.B.Log': 2368, 'A.Other.Write': 50 };

  assert.equal(compareAllocations({ 'A.B.Log': 2368, 'A.Other.Write': 50 }, baseline, null, ['A.B', 'A.Other']).failed, false);
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

test('the operations each allocation was averaged over are read with it, from the job whose figure is kept', () => {
  const dir = mkdtempSync(join(tmpdir(), 'bench-ops-'));
  const report = {
    Benchmarks: [
      { FullName: 'A.B.Log', Memory: { BytesAllocatedPerOperation: 120, TotalOperations: 16 } },
      { FullName: 'A.B.Log', Memory: { BytesAllocatedPerOperation: 100, TotalOperations: 64 } },
    ],
  };
  writeFileSync(join(dir, 'A.B-report-full-compressed.json'), JSON.stringify(report));

  assert.deepEqual(operationsFromJson(dir), { 'A.B.Log': 16 });
});

// ResponseSerializationBenchmarks<T> runs over three documents whose type is called Root; FullName says
// `<Root>` for all three, and the ratchet watched the largest of them only (#55).
test('a generic benchmark over two types of one name is kept apart by its report', () => {
  const dir = mkdtempSync(join(tmpdir(), 'bench-generic-'));
  const entry = (bytes) => ({
    Namespace: 'A',
    Type: 'B<Root>',
    FullName: 'A.B<Root>.Write',
    Memory: { BytesAllocatedPerOperation: bytes },
  });
  writeFileSync(join(dir, 'A.B_Twitter.Root_-report-full-compressed.json'), JSON.stringify({ Benchmarks: [entry(100)] }));
  writeFileSync(join(dir, 'A.B_Canada.Root_-report-full-compressed.json'), JSON.stringify({ Benchmarks: [entry(900)] }));

  assert.deepEqual(allocationsFromJson(dir), { 'A.B_Canada.Root_.Write': 900, 'A.B_Twitter.Root_.Write': 100 });
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

// The separator is the culture's list separator: ';' on an Italian Windows, ',' on the Linux runner,
// where a mean with a thousands separator is quoted. The first run on the runner paired nothing (#55).
test('a comma-separated CSV with quoted cells is read as the runner writes it', () => {
  const csv = [
    'Method,Job,Mean,Error,Allocated',
    'Serilog_Production,DefaultJob,"1,151.8 ns",12.94 ns,3000 B',
    'Serilog_Simple,DefaultJob,312.5 ns,1.02 ns,528 B',
  ].join('\n');

  assert.deepEqual(rowsFromCsv(csv), [
    { method: 'Serilog_Production', job: 'DefaultJob', meanNs: 1151.8 },
    { method: 'Serilog_Simple', job: 'DefaultJob', meanNs: 312.5 },
  ]);
});

// The Result benchmarks measure 0.0000 ns on the runner: below the timer, so their ratio is NaN or
// Infinity, and one of them made the geometric mean of 80 benchmarks NaN (#55).
test('a time below a nanosecond is listed apart and kept out of the ratios', () => {
  const report = abReport(
    { 'R | Empty | DefaultJob': 0, 'R | Log | DefaultJob': 200 },
    { 'R | Empty | DefaultJob': 0.0097, 'R | Log | DefaultJob': 150 },
  );

  assert.match(report, /Geometric mean of the ratios over 1 benchmark\(s\): \*\*0\.750\*\*/);
  assert.match(report, /1 benchmark\(s\) under 1 ns on a side, too fast to compare: `R \| Empty \| DefaultJob`/);
  assert.doesNotMatch(report, /NaN|Infinity/);
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
