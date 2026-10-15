#!/usr/bin/env node
/**
 * Benchmarks in CI: the allocation ratchet, and timings compared A/B in the same job.
 *
 * Why two different treatments. `Allocated` from `[MemoryDiagnoser]` is a property of the code path,
 * so it holds on a noisy shared runner and can fail a run, against a committed baseline, the way the
 * ratchets in `scripts/check.mjs` do. Time is not: a shared runner varies by 10–20% from one run to the
 * next, so timings are only ever compared base against head, built and run on the same VM in the same
 * job, and reported as ratios. Never against a stored history, and never failing anything.
 *
 * ⚠️ The allocations are read from full jobs, never from a cold or dry one: a first call allocates what
 * a warmed-up one does not (21.55 KB cold against 20.17 KB warmed, measured on the endpoint benchmark),
 * and a ratchet fed that would fail on the warmup rather than on the code.
 *
 *   node scripts/benchmarks.mjs ci --head <checkout> --base <checkout> --out <dir> [--changed-only]
 *   node scripts/benchmarks.mjs allocations --results <dir> [--update]
 *   node scripts/benchmarks.mjs ab --base <dir> --head <dir>
 */
import { spawnSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { belongsTo, classesFromList, filtersFor, readCheckout, selectBenchmarks } from './benchmark-selection.mjs';

/**
 * What runs. Each suite with its own jobs, so that a base checkout that knows nothing of this script
 * runs the same thing as the head: Logging's `all` takes no BenchmarkDotNet arguments on an older
 * checkout, and gets the same default job either way.
 */
export const SUITES = [
  {
    name: 'Logging',
    project: 'Pragmatic.Logging/benchmarks/Pragmatic.Logging.Benchmarks/Pragmatic.Logging.Benchmarks.csproj',
    command: ['all'],
  },
  {
    name: 'Result',
    project: 'Pragmatic.Result/benchmarks/Pragmatic.Result.Benchmarks/Pragmatic.Result.Benchmarks.csproj',
    command: [],
  },
  {
    name: 'Endpoints',
    project: 'Pragmatic.Endpoints/benchmarks/Pragmatic.Endpoints.Benchmarks/Pragmatic.Endpoints.Benchmarks.csproj',
    command: [],
  },
  {
    name: 'Documents',
    project: 'Pragmatic.Documents/benchmarks/Pragmatic.Documents.Benchmarks/Pragmatic.Documents.Benchmarks.csproj',
    command: [],
  },
  {
    name: 'Persistence',
    project: 'Pragmatic.Persistence/benchmarks/Pragmatic.Persistence.Benchmarks/Pragmatic.Persistence.Benchmarks.csproj',
    command: [],
  },
];

/** A suite's arguments to BenchmarkDotNet, for every benchmark (`*`) or the filters of a selection. */
export function suiteArgs(suite, filters = ['*']) {
  return [...suite.command, '--filter', ...filters, '--exporters', 'json'];
}

export const BASELINE = 'benchmarks/allocation-baseline.json';

/**
 * How many bytes per operation a benchmark may move without failing the ratchet.
 *
 * One more allocation is at least 24 bytes on x64 (an object header and a method table pointer), so 16
 * still catches it, and leaves room for the averaging of benchmarks that run many operations per
 * invocation and report a fraction.
 */
export const TOLERANCE_BYTES = 16;

/**
 * The share of its baseline a large benchmark may move, when that is more than `TOLERANCE_BYTES`.
 *
 * Allocations are deterministic per code path, not per byte: serialising a large document grows pooled
 * buffers by amounts that, averaged over a handful of operations, are not the same from run to run.
 * Over three runs on the runner the widest move was 270 B on 483 KB (0.056%). A quarter of a percent
 * is four and a half times that (1,207 B there), and still fails on sixty allocations of 24 B on that
 * document, so on anything allocated per element of it.
 */
export const TOLERANCE_SHARE = 0.0025;

/**
 * The benchmarks whose allocations depend on how the thread pool schedules them, each with the share of
 * its baseline it may move. Every entry states the spread measured on the runner; the share covers that
 * and nothing more.
 *
 * `Scopes_Blocking_ConcurrentRequests` reproduces sync-over-async under load: each query blocks on a
 * `Task.Run`, and a wait that outlasts its spin allocates a wait handle, so the bytes follow how many
 * waits the pool left unfinished. Eight runs on the runner gave 134,841 to 145,159 B per operation on a
 * baseline of 134,859, a spread of 7.65% (#227); 8% is 10,789 B. The Pragmatic code it runs is
 * `DefaultUserScopeResolver`, which its direct twin also runs, at 27,832 B in all eight runs and under
 * the ordinary tolerance.
 */
export const SCHEDULER_TOLERANCES = {
  'Pragmatic.Endpoints.Benchmarks.Authorization.ScopeResolutionBenchmarks.Scopes_Blocking_ConcurrentRequests': 0.08,
};

/** The bytes a benchmark with this baseline may move without failing. */
export function toleranceFor(base, name = '') {
  return Math.max(TOLERANCE_BYTES, base * (SCHEDULER_TOLERANCES[name] ?? TOLERANCE_SHARE));
}

// ── Reading BenchmarkDotNet's output ────────────────────────────────────────────────────────────

/** Every file under `dir` whose name ends with `suffix`, recursively. */
export function filesEndingWith(dir, suffix) {
  if (!existsSync(dir)) return [];
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) found.push(...filesEndingWith(path, suffix));
    else if (entry.name.endsWith(suffix)) found.push(path);
  }
  return found;
}

/**
 * Bytes allocated per operation, by benchmark, from the JSON reports under `dir`.
 *
 * A benchmark reported twice (two jobs of one class) keeps its larger figure: the ratchet asks whether
 * anything allocates more, and the larger one is the one that can answer yes.
 */
export function allocationsFromJson(dir) {
  const measured = {};
  for (const file of filesEndingWith(dir, '-report-full-compressed.json')) {
    const report = JSON.parse(readFileSync(file, 'utf8'));
    const reportName = basename(file, '-report-full-compressed.json');
    for (const b of report.Benchmarks ?? []) {
      const bytes = b.Memory?.BytesAllocatedPerOperation;
      if (typeof bytes !== 'number') continue;
      const name = benchmarkName(b, reportName);
      measured[name] = Math.max(measured[name] ?? 0, bytes);
    }
  }
  return measured;
}

/**
 * The benchmark's FullName, with the type named as its report file names it. The two agree for a plain
 * class; for a generic one FullName carries only the type argument's simple name, so
 * `ResponseSerializationBenchmarks<Root>` stood for three documents, and the report name
 * (`ResponseSerializationBenchmarks_Twitter.Root_`) is what tells them apart.
 */
function benchmarkName(benchmark, reportName) {
  const type = `${benchmark.Namespace}.${benchmark.Type}`;
  return benchmark.FullName.startsWith(type)
    ? reportName + benchmark.FullName.slice(type.length)
    : benchmark.FullName;
}

const TIME_UNITS = { ns: 1, 'μs': 1e3, 'µs': 1e3, us: 1e3, ms: 1e6, s: 1e9 };

/** A BenchmarkDotNet time cell ("1,093.9 μs") in nanoseconds, or null for "NA" and the like. */
export function parseTime(cell) {
  const m = /^\s*([\d,.]+)\s*(ns|μs|µs|us|ms|s)\s*$/.exec(cell ?? '');
  if (!m) return null;
  return Number(m[1].replaceAll(',', '')) * TIME_UNITS[m[2]];
}

/**
 * One CSV line in cells. BenchmarkDotNet separates with the culture's list separator (';' on an Italian
 * Windows, ',' on the Linux runner) and quotes a cell that contains it, as in "1,151.8 ns".
 */
function splitCsvLine(line, separator) {
  const cells = [];
  let cell = '';
  let quoted = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quoted) {
      if (c === '"' && line[i + 1] === '"') (cell += '"', i++);
      else if (c === '"') quoted = false;
      else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === separator) (cells.push(cell), (cell = ''));
    else cell += c;
  }
  cells.push(cell);
  return cells;
}

/** The rows of a BenchmarkDotNet CSV summary: Method, Job and the mean in nanoseconds. */
export function rowsFromCsv(text) {
  const [header, ...rest] = text.split(/\r?\n/).filter((l) => l.length > 0);
  // The header is always "Method" and then the separator.
  const separator = header?.charAt('Method'.length);
  if (!separator) return [];
  const columns = splitCsvLine(header, separator);
  const lines = rest.map((line) => splitCsvLine(line, separator));
  const at = (name) => columns.indexOf(name);
  const [method, job, mean] = [at('Method'), at('Job'), at('Mean')];
  if (method < 0 || mean < 0) return [];

  // Parameter columns, when a benchmark has them, sit between the job's settings and Mean; they are
  // what tells two rows of one method apart.
  const parameters = columns
    .map((name, i) => ({ name, i }))
    .filter(({ i }) => i > (job < 0 ? method : job) && i < mean && !JOB_COLUMNS.has(columns[i]));

  return lines.map((cells) => {
    const params = parameters.map(({ name, i }) => `${name}=${cells[i]}`).join(',');
    return {
      method: cells[method] + (params ? `(${params})` : ''),
      job: job < 0 ? '' : cells[job],
      meanNs: parseTime(cells[mean]),
    };
  });
}

// The job characteristics BenchmarkDotNet writes into every CSV row, which are not parameters.
const JOB_COLUMNS = new Set([
  'AnalyzeLaunchVariance', 'EvaluateOverhead', 'MaxAbsoluteError', 'MaxRelativeError', 'MinInvokeCount',
  'MinIterationTime', 'OutlierMode', 'Affinity', 'EnvironmentVariables', 'Jit', 'LargeAddressAware',
  'Platform', 'PowerPlanMode', 'Runtime', 'AllowVeryLargeObjects', 'Concurrent', 'CpuGroups', 'Force',
  'HeapAffinitizeMask', 'HeapCount', 'NoAffinitize', 'RetainVm', 'Server', 'Arguments',
  'BuildConfiguration', 'Clock', 'EngineFactory', 'NuGetReferences', 'Toolchain', 'IsMutator',
  'InvocationCount', 'IterationCount', 'IterationTime', 'LaunchCount', 'MaxIterationCount',
  'MaxWarmupIterationCount', 'MemoryRandomization', 'MinIterationCount', 'MinWarmupIterationCount',
  'RunStrategy', 'UnrollFactor', 'WarmupCount',
]);

/** Mean times by "report | method | job", from the CSV summaries under `dir`. */
export function timesFromCsv(dir) {
  const times = {};
  for (const file of filesEndingWith(dir, '-report.csv')) {
    const report = basename(file, '-report.csv');
    for (const row of rowsFromCsv(readFileSync(file, 'utf8'))) {
      if (row.meanNs !== null) times[`${report} | ${row.method} | ${row.job}`] = row.meanNs;
    }
  }
  return times;
}

// ── The allocation ratchet ──────────────────────────────────────────────────────────────────────

/**
 * Compares measured allocations with the baseline.
 *
 * - `regressions`: allocates more than the baseline, beyond the tolerance. Fails.
 * - `unbaselined`: measured, with no baseline. Fails: a new benchmark gets a baseline in the same change.
 * - `improvements`: allocates less, beyond the tolerance. Passes, and asks for the baseline to come down.
 * - `missing`: in the baseline, not measured. Fails: BenchmarkDotNet exits 0 when it cannot build a
 *   benchmark, so one that did not run is only visible here. A benchmark removed or renamed takes its
 *   baseline entry with it in the same change.
 * - `notRun`: a class the suite lists for this run that measured nothing. Fails, for the same reason,
 *   and it also catches a class that has no baseline to go missing from: one new, or one BenchmarkDotNet
 *   skipped because its setup threw, every night since it was added (#183).
 *
 * `classes`, on a partial run, are the classes that ran: the baseline of every other class is not
 * looked at, since nothing measured it. `listed` are the classes the suites list for the run, the
 * selected ones on a partial run and every one on a full run, where `classes` is null.
 */
export function compareAllocations(measured, baseline, classes = null, listed = classes) {
  const result = { regressions: [], unbaselined: [], improvements: [], missing: [], notRun: [] };
  const judged = classes === null
    ? baseline
    : Object.fromEntries(Object.entries(baseline).filter(([name]) => classes.some((c) => belongsTo(name, c))));
  for (const [name, bytes] of Object.entries(measured).sort()) {
    const base = judged[name];
    if (base === undefined) result.unbaselined.push({ name, bytes });
    else if (bytes > base + toleranceFor(base, name)) result.regressions.push({ name, bytes, base });
    else if (bytes < base - toleranceFor(base, name)) result.improvements.push({ name, bytes, base });
  }
  for (const name of Object.keys(judged).sort()) {
    if (!(name in measured)) result.missing.push({ name, base: judged[name] });
  }
  for (const c of listed ?? []) {
    if (!Object.keys(measured).some((name) => belongsTo(name, c))) result.notRun.push({ name: c });
  }
  result.failed = result.regressions.length > 0 || result.unbaselined.length > 0 || result.missing.length > 0
    || result.notRun.length > 0;
  return result;
}

/** The comparison, as the markdown the job summary shows. */
export function allocationReport(result, measuredCount) {
  const lines = ['## Allocations', ''];
  const row = (r) => `| \`${r.name}\` | ${r.base ?? '—'} B | ${r.bytes ?? '—'} B |`;
  const table = (title, items) => {
    if (items.length === 0) return;
    lines.push(`### ${title}`, '', '| Benchmark | Baseline | Measured |', '|---|---:|---:|', ...items.map(row), '');
  };

  lines.push(result.failed
    ? `**FAIL**: ${result.regressions.length} allocate more than the baseline, ${result.unbaselined.length} have no baseline, ${result.missing.length} in the baseline did not run, ${result.notRun.length} listed class(es) measured nothing.`
    : measuredCount === 0
      ? '**FAIL**: no benchmark was measured.'
      : `**ok**: ${measuredCount} benchmark(s), none allocates more than the baseline (tolerance ${TOLERANCE_BYTES} B or ${TOLERANCE_SHARE * 100}%, whichever is larger; ${Object.keys(SCHEDULER_TOLERANCES).length} scheduled row(s) have their own, in \`SCHEDULER_TOLERANCES\`).`, '');
  table('Allocates more (fails)', result.regressions);
  table('No baseline (fails)', result.unbaselined);
  table('In the baseline, did not run (fails)', result.missing);
  table('Listed, measured nothing (fails)', result.notRun);
  table('Allocates less: lower the baseline', result.improvements);
  return lines.join('\n');
}

// ── A/B timing ──────────────────────────────────────────────────────────────────────────────────

/**
 * Below this a mean is the timer's resolution, not the code: BenchmarkDotNet reports 0.0000 ns for a
 * method it cannot tell from an empty one, and the ratio of two such means is noise, NaN or Infinity.
 */
const COMPARABLE_NS = 1;

/** Head against base, as ratios: the markdown the job summary shows. Never a verdict. */
export function abReport(baseTimes, headTimes) {
  const paired = Object.keys(headTimes).filter((k) => k in baseTimes).sort();
  const tooFast = paired.filter((k) => baseTimes[k] < COMPARABLE_NS || headTimes[k] < COMPARABLE_NS);
  const keys = paired.filter((k) => !tooFast.includes(k));
  const lines = [
    '## Timings, head against base',
    '',
    'Same runner, same job, each suite run once on each side with the order alternated between suites. '
      + 'A shared runner varies by 10–20% between runs, so a ratio inside that band says nothing; this '
      + 'table never fails the job.',
    '',
  ];
  const tooFastNote = tooFast.length > 0
    ? [`${tooFast.length} benchmark(s) under ${COMPARABLE_NS} ns on a side, too fast to compare: ${tooFast.map((k) => `\`${k}\``).join(', ')}.`]
    : [];
  if (keys.length === 0) {
    lines.push(paired.length === 0 ? 'No benchmark ran on both sides.' : 'No benchmark is slow enough on both sides to compare.');
    if (tooFastNote.length > 0) lines.push('', ...tooFastNote);
    return lines.join('\n');
  }

  lines.push('| Benchmark | Base | Head | Head / base |', '|---|---:|---:|---:|');
  let logSum = 0;
  for (const key of keys) {
    const ratio = headTimes[key] / baseTimes[key];
    logSum += Math.log(ratio);
    lines.push(`| ${key} | ${formatNs(baseTimes[key])} | ${formatNs(headTimes[key])} | ${ratio.toFixed(2)} |`);
  }
  lines.push('', `Geometric mean of the ratios over ${keys.length} benchmark(s): **${Math.exp(logSum / keys.length).toFixed(3)}**.`);
  if (tooFastNote.length > 0) lines.push('', ...tooFastNote);

  const onlyHead = Object.keys(headTimes).filter((k) => !(k in baseTimes));
  if (onlyHead.length > 0) lines.push('', `${onlyHead.length} benchmark(s) ran on the head only and are not compared.`);
  return lines.join('\n');
}

function formatNs(ns) {
  if (ns >= 1e6) return `${(ns / 1e6).toFixed(3)} ms`;
  if (ns >= 1e3) return `${(ns / 1e3).toFixed(2)} μs`;
  return `${ns.toFixed(1)} ns`;
}

// ── Running the suites ──────────────────────────────────────────────────────────────────────────

/**
 * Runs one suite of one checkout from the project's own directory, then moves BenchmarkDotNet's
 * artifacts into `workDir`.
 *
 * ⚠️ The working directory is not a choice: BenchmarkDotNet finds the project to build its boilerplate
 * from by searching the working directory, and from anywhere else it builds nothing — and still exits 0
 * (the first run of the workflow, #55). Nor can `--artifacts` put the reports elsewhere, since a base
 * checkout's program may not pass its arguments on; moving them afterwards works for any checkout.
 */
function runSuite(checkout, suite, workDir, filters) {
  const project = resolve(checkout, suite.project);
  if (!existsSync(project)) return { ran: false, reason: 'not in this checkout' };

  const projectDir = dirname(project);
  const artifacts = join(projectDir, 'BenchmarkDotNet.Artifacts');
  rmSync(artifacts, { recursive: true, force: true });

  const r = spawnSync('dotnet', ['run', '-c', 'Release', '--project', project, '--', ...suiteArgs(suite, filters)], {
    cwd: projectDir,
    stdio: 'inherit',
  });

  mkdirSync(workDir, { recursive: true });
  if (existsSync(artifacts)) renameSync(artifacts, join(workDir, 'BenchmarkDotNet.Artifacts'));
  return { ran: true, code: r.status ?? 1 };
}

/**
 * The selected classes the head's suite actually runs, from BenchmarkDotNet's `--list flat`: a class
 * the change reaches may be one the suite never runs (Logging's probes), and one the suite runs must
 * then measure something.
 */
function listSuite(checkout, suite, filters) {
  const project = resolve(checkout, suite.project);
  const r = spawnSync('dotnet', ['run', '-c', 'Release', '--project', project, '--', ...suite.command, '--list', 'flat', '--filter', ...filters], {
    cwd: dirname(project),
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
  });
  if (r.status !== 0) process.stderr.write(r.stdout + r.stderr);
  return { code: r.status ?? 1, classes: classesFromList(r.stdout ?? '') };
}

/**
 * Per suite, the filters to run with (`null` for a suite with nothing to run); the classes that run on a
 * partial run (`null` on a full one); and the classes the suites list for the run, each of which must
 * measure something.
 */
function plan(options, notes) {
  const everything = Object.fromEntries(SUITES.map((s) => [s.name, ['*']]));
  // A full run judged no class by itself: one BenchmarkDotNet skipped had nothing in the baseline to go
  // missing from, and the nightly run stayed green (#183). Listing every suite names each class.
  const listEverything = () => SUITES.flatMap((suite) => {
    const listed = listSuite(options.head, suite, ['*']);
    if (listed.code !== 0) throw new Error(`${suite.name}: listing the benchmarks exited ${listed.code}.`);
    return listed.classes;
  });
  if (!options['changed-only']) return { filters: everything, classes: null, listed: listEverything() };

  const selection = selectBenchmarks(readCheckout(options.head, SUITES), readCheckout(options.base, SUITES), SUITES);
  if (selection.all) {
    notes.push(`Every benchmark runs: ${selection.all}.`);
    return { filters: everything, classes: null, listed: listEverything() };
  }

  const filters = {};
  const classes = [];
  for (const suite of SUITES) {
    const s = selection.suites[suite.name];
    if (s.all) {
      notes.push(`${suite.name}: every benchmark runs, ${s.all}.`);
      filters[suite.name] = ['*'];
      // Every class of the suite is judged: listing everything names them.
      classes.push(...listSuite(options.head, suite, ['*']).classes);
      continue;
    }
    const listed = s.classes.length === 0 ? { code: 0, classes: [] } : listSuite(options.head, suite, filtersFor(s.classes));
    if (listed.code !== 0) throw new Error(`${suite.name}: listing the selected benchmarks exited ${listed.code}.`);
    const skipped = s.classes.filter((c) => !listed.classes.includes(c));
    if (skipped.length > 0) notes.push(`${suite.name}: reached, but not in the suite's run: ${skipped.map((c) => `\`${c}\``).join(', ')}.`);
    filters[suite.name] = listed.classes.length > 0 ? filtersFor(listed.classes) : null;
    classes.push(...listed.classes);
    notes.push(listed.classes.length > 0
      ? `${suite.name}: runs ${listed.classes.map((c) => `\`${c}\``).join(', ')}.`
      : `${suite.name}: nothing this change reaches.`);
  }
  notes.unshift('Partial run, for a pull request: only the classes the change reaches. The nightly run measures everything.');
  return { filters, classes, listed: classes };
}

function ci(options) {
  const out = resolve(options.out);
  const notes = [];
  let headFailed = false;
  const { filters, classes, listed } = plan(options, notes);

  SUITES.forEach((suite, i) => {
    if (filters[suite.name] === null) return;
    // Alternated between suites, so that a runner warming up or slowing down over the job does not
    // land on one side every time.
    const order = i % 2 === 0 ? ['base', 'head'] : ['head', 'base'];
    for (const side of order) {
      const r = runSuite(options[side], suite, join(out, side, suite.name), filters[suite.name]);
      if (!r.ran) notes.push(`${suite.name} on ${side}: ${r.reason}.`);
      else if (r.code !== 0) {
        notes.push(`${suite.name} on ${side}: exited ${r.code}.`);
        if (side === 'head') headFailed = true;
      }
    }
  });

  const measured = allocationsFromJson(join(out, 'head'));
  const baseline = existsSync(join(options.head, BASELINE))
    ? JSON.parse(readFileSync(join(options.head, BASELINE), 'utf8'))
    : {};
  const allocations = compareAllocations(measured, baseline, classes, listed);
  const nothingToRun = classes !== null && classes.length === 0;

  // What the baseline would be if this run were accepted: uploaded with the reports, so a new or
  // lowered baseline is copied from a run on the runner rather than typed in from a laptop. On a
  // partial run it is the committed baseline with the classes that ran replaced, so it is whole.
  const proposed = classes === null
    ? measured
    : { ...Object.fromEntries(Object.entries(baseline).filter(([name]) => !classes.some((c) => belongsTo(name, c)))), ...measured };
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, 'allocation-baseline.proposed.json'), JSON.stringify(sorted(proposed), null, 2) + '\n');

  const summary = [
    '# Benchmarks',
    '',
    ...(notes.length > 0 ? ['Runs:', '', ...notes.map((n) => `- ${n}`), ''] : []),
    nothingToRun ? '## Allocations\n\n**ok**: the change reaches no benchmark the suites run.' : allocationReport(allocations, Object.keys(measured).length),
    '',
    abReport(timesFromCsv(join(out, 'base')), timesFromCsv(join(out, 'head'))),
    '',
  ].join('\n');

  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary);
  console.log(summary);

  if (nothingToRun) return 0;
  return headFailed || allocations.failed || Object.keys(measured).length === 0 ? 1 : 0;
}

function sorted(object) {
  return Object.fromEntries(Object.entries(object).sort(([a], [b]) => a.localeCompare(b)));
}

function parseOptions(argv) {
  const options = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith('--')) continue;
    const next = argv[i + 1];
    options[arg.slice(2)] = next && !next.startsWith('--') ? (i++, next) : true;
  }
  return options;
}

function main(argv) {
  const [command, ...rest] = argv;
  const options = parseOptions(rest);

  switch (command) {
    case 'ci':
      return ci({
        head: resolve(options.head ?? '.'),
        base: resolve(options.base ?? '.'),
        out: options.out ?? 'artifacts/benchmarks',
        'changed-only': options['changed-only'] === true,
      });

    case 'allocations': {
      const measured = allocationsFromJson(options.results);
      if (options.update) {
        writeFileSync(BASELINE, JSON.stringify(sorted(measured), null, 2) + '\n');
        console.log(`Wrote ${Object.keys(measured).length} baseline(s) to ${BASELINE}.`);
        return 0;
      }
      const baseline = existsSync(BASELINE) ? JSON.parse(readFileSync(BASELINE, 'utf8')) : {};
      const result = compareAllocations(measured, baseline);
      console.log(allocationReport(result, Object.keys(measured).length));
      return result.failed ? 1 : 0;
    }

    case 'ab':
      console.log(abReport(timesFromCsv(options.base), timesFromCsv(options.head)));
      return 0;

    default:
      console.error('usage: node scripts/benchmarks.mjs ci|allocations|ab [options]');
      return 2;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
