/**
 * Which benchmark classes a pull request reaches (#172), on checkouts held in memory.
 *
 * ⚠️ What this covers and what it does not. It proves the selection reaches a benchmark through its own
 * file, through helpers, through a partial file and through its baseline entry, and falls back to the
 * whole suite or to everything where it cannot tell. That BenchmarkDotNet then runs exactly the
 * selected classes is proven by the workflow run on a pull request.
 *
 *   node --test scripts/benchmark-selection.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  BASELINE_PATH,
  belongsTo,
  changedBaselineEntries,
  classesFromList,
  declarations,
  filtersFor,
  selectBenchmarks,
} from './benchmark-selection.mjs';

const SUITES = [
  { name: 'Logging', project: 'L/benchmarks/L.Benchmarks/L.Benchmarks.csproj', command: ['all'] },
  { name: 'Result', project: 'R/benchmarks/R.Benchmarks/R.Benchmarks.csproj', command: [] },
];

const DIR = 'L/benchmarks/L.Benchmarks/';

/** A checkout with two Logging classes sharing nothing, one through a chain of helpers, and Result. */
function checkout() {
  return new Map([
    ['scripts/benchmarks.mjs', 'harness'],
    [BASELINE_PATH, JSON.stringify({ 'L.Benchmarks.Json.JsonBenchmarks.Write': 100, 'R.Benchmarks.ResultBenchmarks.Map': 0 })],
    [`${DIR}L.Benchmarks.csproj`, '<Project />'],
    [`${DIR}Program.cs`, 'namespace L.Benchmarks;\npublic class Program { void Main() { Run(typeof(Json.JsonBenchmarks)); Run(typeof(Modules.ActionsBenchmarks)); } }\npublic class SharedConfig { }'],
    [`${DIR}Json/JsonBenchmarks.cs`, 'namespace L.Benchmarks.Json;\npublic class JsonBenchmarks { private readonly LastLine _line = new(); [Benchmark] public void Write() {} }'],
    [`${DIR}Json/LastLine.cs`, 'namespace L.Benchmarks.Json;\ninternal sealed class LastLine { private readonly LineBuffer _b = new(); }'],
    [`${DIR}Json/LineBuffer.cs`, 'namespace L.Benchmarks.Json;\ninternal sealed class LineBuffer { }'],
    [`${DIR}Modules/ActionsBenchmarks.cs`, 'namespace L.Benchmarks.Modules;\n[Config(typeof(SharedConfig))]\npublic partial class ActionsBenchmarks { [Benchmark(Baseline = true)] public void Log() {} }'],
    [`${DIR}Modules/ActionsBenchmarks.Verification.cs`, 'namespace L.Benchmarks.Modules;\npublic partial class ActionsBenchmarks { void Verify() {} }'],
    // Rows in an abstract class, run by classes that only derive from it, one per BenchmarkDotNet job (#215).
    [`${DIR}Probe/ProbeRows.cs`, 'namespace L.Benchmarks.Probe;\npublic abstract class ProbeRows { [Benchmark] public void Row() {} }'],
    [`${DIR}Probe/ProbeTiered.cs`, 'namespace L.Benchmarks.Probe;\n[SimpleJob(iterationCount: 60)]\npublic class ProbeTiered : ProbeRows;'],
    [`${DIR}Probe/ProbeNoTiering.cs`, 'namespace L.Benchmarks.Probe;\n[Config(typeof(NoTiering))]\npublic class ProbeNoTiering : Probe.ProbeRows;'],
    ['R/benchmarks/R.Benchmarks/ResultBenchmarks.cs', 'namespace R.Benchmarks;\npublic class ResultBenchmarks { [Benchmark] public void Map() {} }'],
    ['R/benchmarks/reports/old-report-github.md', 'a report'],
  ]);
}

function changed(edits) {
  const head = checkout();
  for (const [path, text] of Object.entries(edits)) {
    if (text === null) head.delete(path);
    else head.set(path, text);
  }
  return selectBenchmarks(head, checkout(), SUITES);
}

test('a changed benchmark file selects its class, and nothing in another suite', () => {
  const selection = changed({ [`${DIR}Modules/ActionsBenchmarks.cs`]: 'namespace L.Benchmarks.Modules;\npublic partial class ActionsBenchmarks { [Benchmark] public void Log2() {} }' });

  assert.deepEqual(selection, {
    suites: { Logging: { classes: ['L.Benchmarks.Modules.ActionsBenchmarks'] }, Result: { classes: [] } },
  });
});

test('a new benchmark file selects its class', () => {
  const selection = changed({ [`${DIR}Modules/MessagingBenchmarks.cs`]: 'namespace L.Benchmarks.Modules;\npublic class MessagingBenchmarks { [Benchmark] public void Log() {} }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Modules.MessagingBenchmarks']);
});

test('a helper two files away from a benchmark selects that benchmark only', () => {
  const selection = changed({ [`${DIR}Json/LineBuffer.cs`]: 'namespace L.Benchmarks.Json;\ninternal sealed class LineBuffer { int _n; }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Json.JsonBenchmarks']);
});

test('the partial half of a benchmark class without [Benchmark] selects the class', () => {
  const selection = changed({ [`${DIR}Modules/ActionsBenchmarks.Verification.cs`]: 'namespace L.Benchmarks.Modules;\npublic partial class ActionsBenchmarks { void Verify2() {} }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Modules.ActionsBenchmarks']);
});

// Program names every benchmark class it runs, and here also declares a config another class uses: if a
// changed benchmark class spread to what names it, it would reach every class through Program.
test('a benchmark class does not spread to what names it', () => {
  const selection = changed({ [`${DIR}Json/JsonBenchmarks.cs`]: 'namespace L.Benchmarks.Json;\npublic class JsonBenchmarks { [Benchmark] public void Write2() {} }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Json.JsonBenchmarks']);
});

test('a helper declared next to the entry point selects the classes that use it', () => {
  const selection = changed({ [`${DIR}Program.cs`]: 'namespace L.Benchmarks;\npublic class Program { }\npublic class SharedConfig { int _jobs; }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Modules.ActionsBenchmarks']);
});

// The file of a class that only derives holds no [Benchmark]; read as a helper, it reached nothing, and
// #214's probe ran no benchmark on its pull request while the job reported ok.
test('a class whose benchmarks are inherited is a benchmark class', () => {
  const selection = changed({ [`${DIR}Probe/ProbeTiered.cs`]: 'namespace L.Benchmarks.Probe;\n[SimpleJob(iterationCount: 90)]\npublic class ProbeTiered : ProbeRows;' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Probe.ProbeTiered']);
});

test('a changed base selects every class that derives from it', () => {
  const selection = changed({ [`${DIR}Probe/ProbeRows.cs`]: 'namespace L.Benchmarks.Probe;\npublic abstract class ProbeRows { [Benchmark] public void Row2() {} }' });

  assert.deepEqual(selection.suites.Logging.classes,
    ['L.Benchmarks.Probe.ProbeNoTiering', 'L.Benchmarks.Probe.ProbeRows', 'L.Benchmarks.Probe.ProbeTiered']);
});

test('a class two derivations away from the rows is a benchmark class too', () => {
  const selection = changed({
    [`${DIR}Probe/ProbeTieredLonger.cs`]: 'namespace L.Benchmarks.Probe;\npublic sealed class ProbeTieredLonger : ProbeTiered;',
  });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Probe.ProbeTieredLonger']);
});

test('a changed baseline entry selects the class it belongs to', () => {
  const baseline = JSON.stringify({ 'L.Benchmarks.Json.JsonBenchmarks.Write': 90, 'R.Benchmarks.ResultBenchmarks.Map': 0 });

  const selection = changed({ [BASELINE_PATH]: baseline });

  assert.deepEqual(selection.suites, { Logging: { classes: ['L.Benchmarks.Json.JsonBenchmarks'] }, Result: { classes: [] } });
});

test('a project file selects the whole suite, and the other suite stays as it was', () => {
  const selection = changed({ [`${DIR}L.Benchmarks.csproj`]: '<Project Sdk="x" />' });

  assert.deepEqual(selection.suites.Logging, { all: `${DIR}L.Benchmarks.csproj changed`, classes: [] });
  assert.deepEqual(selection.suites.Result, { classes: [] });
});

test('a deleted C# file selects the whole suite', () => {
  const selection = changed({ [`${DIR}Json/LineBuffer.cs`]: null });

  assert.deepEqual(selection.suites.Logging, { all: `${DIR}Json/LineBuffer.cs was deleted`, classes: [] });
});

test('a change to the harness runs everything', () => {
  assert.deepEqual(changed({ 'scripts/benchmarks.mjs': 'harness, changed' }), { all: 'scripts/benchmarks.mjs changed' });
});

test('a report next to the benchmarks selects nothing', () => {
  const selection = changed({ 'R/benchmarks/reports/old-report-github.md': 'a newer report' });

  assert.deepEqual(selection.suites, { Logging: { classes: [] }, Result: { classes: [] } });
});

test('a type named in a comment is not a declaration', () => {
  const { types } = declarations('namespace N;\n// this class handles the rest\n/* a struct Foo */ public record struct Point(int X); public class Box<T> where T : class { }');

  assert.deepEqual(types.sort(), ['Box', 'Point']);
});

test('a key belongs to its class, and to its generic class through the report name', () => {
  assert.equal(belongsTo('L.B.ProbeBenchmarks.Write', 'L.B.ProbeBenchmarks'), true);
  assert.equal(belongsTo('L.B.ClassicProbeBenchmarks.Write', 'L.B.ProbeBenchmarks'), false);
  assert.equal(belongsTo('E.B.ResponseBenchmarks_Twitter.Root_.Write', 'E.B.ResponseBenchmarks'), true);
  assert.equal(belongsTo('E.B.ResponseBenchmarksMore.Write', 'E.B.ResponseBenchmarks'), false);
});

test('the filters of a class match it as a plain and as a generic class', () => {
  assert.deepEqual(filtersFor(['E.B.Response']), ['E.B.Response.*', 'E.B.Response<*']);
});

test('the classes of a listing are read among the build lines', () => {
  const output = [
    'C:\\repo\\Some.cs(37,20): warning IL2026: Using member x [C:\\repo\\P.csproj]',
    '🚀 Complete benchmark suite',
    "Filters 'L.Benchmarks.Modules.ActionsBenchmarks.*' that you have provided returned 0 benchmarks.",
    '\tL.Benchmarks.Json.JsonBenchmarks.Write',
    'L.Benchmarks.Modules.ActionsBenchmarks.Log',
    'L.Benchmarks.Modules.ActionsBenchmarks.Log2',
    'E.Benchmarks.ResponseBenchmarks<Twitter.Root>.Write',
    'E.Benchmarks.ResponseBenchmarks<Canada.Root>.Write',
  ].join('\r\n');

  assert.deepEqual(classesFromList(output), ['E.Benchmarks.ResponseBenchmarks', 'L.Benchmarks.Modules.ActionsBenchmarks']);
});

test('the changed baseline entries are those added, removed or moved', () => {
  const base = JSON.stringify({ a: 1, b: 2, c: 3 });
  const head = JSON.stringify({ a: 1, b: 5, d: 4 });

  assert.deepEqual(changedBaselineEntries(head, base), ['b', 'c', 'd']);
});
