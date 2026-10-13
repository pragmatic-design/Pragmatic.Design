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
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import {
  BASELINE_PATH,
  belongsTo,
  changedBaselineEntries,
  classesFromList,
  declarations,
  filtersFor,
  readCheckout,
  selectBenchmarks,
} from './benchmark-selection.mjs';

const SUITES = [
  { name: 'Logging', project: 'L/benchmarks/L.Benchmarks/L.Benchmarks.csproj', command: ['all'] },
  { name: 'Result', project: 'R/benchmarks/R.Benchmarks/R.Benchmarks.csproj', command: [] },
];

const DIR = 'L/benchmarks/L.Benchmarks/';

/**
 * The Logging benchmarks reference their module, which references Core; the generator comes in as an
 * analyzer, over a line break as the real project files write it. Nothing references M.
 */
const BENCHMARK_PROJECT = `<Project>
  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\L\\L.csproj" />
    <ProjectReference Include="..\\..\\..\\Gen\\src\\Gen\\Gen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>`;

/** A checkout with two Logging classes sharing nothing, one through a chain of helpers, and Result. */
function checkout() {
  return new Map([
    ['scripts/benchmarks.mjs', 'harness'],
    ['Directory.Packages.props', '<Project />'],
    [BASELINE_PATH, JSON.stringify({ 'L.Benchmarks.Json.JsonBenchmarks.Write': 100, 'R.Benchmarks.ResultBenchmarks.Map': 0 })],
    [`${DIR}L.Benchmarks.csproj`, BENCHMARK_PROJECT],
    [`${DIR}Context/ContextBenchmarks.cs`, 'namespace L.Benchmarks.Context;\npublic class ContextBenchmarks { [Benchmark] public void Push() { using var s = LogContextScope.Push(); } }'],
    ['L/src/L/L.csproj', '<Project><ItemGroup><ProjectReference Include="..\\..\\..\\Core\\src\\Core\\Core.csproj" /></ItemGroup></Project>'],
    ['L/src/L/Context/LogContextScope.cs', 'namespace L.Context;\npublic sealed class LogContextScope { private readonly Clock _clock = new(); }'],
    ['L/src/L/Context/LogContext.cs', 'namespace L.Context;\npublic sealed class LogContext { }'],
    ['Core/src/Core/Core.csproj', '<Project />'],
    ['Core/src/Core/Clock.cs', 'namespace Core;\npublic sealed class Clock { }'],
    ['Gen/src/Gen/Gen.csproj', '<Project />'],
    ['Gen/src/Gen/Templates/WriterTemplate.cs', 'namespace Gen.Templates;\ninternal sealed class WriterTemplate { }'],
    ['shared/SourceGen/CSharpTemplate.cs', 'namespace Shared;\npublic abstract class CSharpTemplate { }'],
    ['M/src/M/M.csproj', '<Project />'],
    ['M/src/M/Thing.cs', 'namespace M;\npublic sealed class Thing { }'],
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

// #188 changed LogContextScope under Pragmatic.Logging/src, and the job read only the benchmark project:
// it reported "nothing this change reaches" for a class that pushes a context on every row (#189).
test('a change to a type in a referenced module selects the benchmark class that uses it', () => {
  const selection = changed({ 'L/src/L/Context/LogContextScope.cs': 'namespace L.Context;\npublic sealed class LogContextScope { private readonly Clock _clock = new(); int _depth; }' });

  assert.deepEqual(selection.suites, { Logging: { classes: ['L.Benchmarks.Context.ContextBenchmarks'] }, Result: { classes: [] } });
});

test('a change in a module the referenced module references reaches through both', () => {
  const selection = changed({ 'Core/src/Core/Clock.cs': 'namespace Core;\npublic sealed class Clock { long _ticks; }' });

  assert.deepEqual(selection.suites.Logging.classes, ['L.Benchmarks.Context.ContextBenchmarks']);
});

test('a change in a referenced module that no benchmark uses selects nothing', () => {
  const selection = changed({ 'L/src/L/Context/LogContext.cs': 'namespace L.Context;\npublic sealed class LogContext { int _n; }' });

  assert.deepEqual(selection.suites, { Logging: { classes: [] }, Result: { classes: [] } });
});

test('a change to a module no benchmark project references selects nothing', () => {
  const selection = changed({ 'M/src/M/Thing.cs': 'namespace M;\npublic sealed class Thing { int _n; }', 'M/src/M/M.csproj': '<Project Sdk="x" />' });

  assert.deepEqual(selection.suites, { Logging: { classes: [] }, Result: { classes: [] } });
});

test('a referenced project file selects the whole suite, as the benchmark project file does', () => {
  const selection = changed({ 'Core/src/Core/Core.csproj': '<Project Sdk="x" />' });

  assert.deepEqual(selection.suites.Logging, { all: 'Core/src/Core/Core.csproj changed', classes: [] });
});

// A generator writes code into the benchmarks and the modules; no benchmark names a template, so its
// reach cannot be read from the sources.
test('a change to a generator the suite is built with selects the whole suite', () => {
  const selection = changed({ 'Gen/src/Gen/Templates/WriterTemplate.cs': 'namespace Gen.Templates;\ninternal sealed class WriterTemplate { int _n; }' });

  assert.deepEqual(selection.suites.Logging, { all: 'Gen/src/Gen/Templates/WriterTemplate.cs changed', classes: [] });
  assert.deepEqual(selection.suites.Result, { classes: [] });
});

test('the shared code compiled into every generator selects the suites built with one', () => {
  const selection = changed({ 'shared/SourceGen/CSharpTemplate.cs': 'namespace Shared;\npublic abstract class CSharpTemplate { int _n; }' });

  assert.deepEqual(selection.suites.Logging, { all: 'shared/SourceGen/CSharpTemplate.cs changed', classes: [] });
  assert.deepEqual(selection.suites.Result, { classes: [] });
});

test('a file every project is built with runs everything', () => {
  assert.deepEqual(changed({ 'Directory.Packages.props': '<Project Sdk="x" />' }), { all: 'Directory.Packages.props changed' });
});

test('a reference through an MSBuild property the selection cannot resolve selects the whole suite', () => {
  const selection = changed({ 'L/src/L/L.csproj': '<Project><ItemGroup><ProjectReference Include="$(Elsewhere)X.csproj" /></ItemGroup></Project>' });

  assert.deepEqual(selection.suites.Logging, { all: 'L/src/L/L.csproj changed', classes: [] });
  const unresolved = selectBenchmarks(
    new Map([...checkout(), ['L/src/L/L.csproj', '<Project><ItemGroup><ProjectReference Include="$(Elsewhere)X.csproj" /></ItemGroup></Project>']]),
    new Map([...checkout(), ['L/src/L/L.csproj', '<Project><ItemGroup><ProjectReference Include="$(Elsewhere)X.csproj" /></ItemGroup></Project>']]),
    SUITES);
  assert.equal(unresolved.suites.Logging.all, 'L/src/L/L.csproj references $(Elsewhere)X.csproj, which the selection cannot resolve');
});

test('the referenced modules are read from the checkout on disk, and an unreferenced one is not', () => {
  const root = mkdtempSync(join(tmpdir(), 'bench-checkout-'));
  for (const [path, text] of checkout()) {
    mkdirSync(join(root, dirname(path)), { recursive: true });
    writeFileSync(join(root, path), text);
  }

  const files = readCheckout(root, SUITES);

  for (const path of ['L/src/L/Context/LogContextScope.cs', 'Core/src/Core/Clock.cs', 'Gen/src/Gen/Templates/WriterTemplate.cs', 'shared/SourceGen/CSharpTemplate.cs', 'Directory.Packages.props']) {
    assert.equal(files.get(path), checkout().get(path), path);
  }
  assert.equal(files.has('M/src/M/Thing.cs'), false);
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
