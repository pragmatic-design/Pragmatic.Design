/**
 * Which benchmark classes a pull request reaches, so that the benchmarks job runs those and not the
 * whole of every suite (#172). The nightly and on-demand runs stay complete; this is only for a pull
 * request, where a run of everything took 2h15m to prove one new class.
 *
 * What changed is read by comparing the two checkouts the job already has, file by file, rather than
 * from git: the checkouts are shallow, and a file that differs because main moved on is selected too.
 * Every rule here can select more than the change needs, never less:
 *
 * - a changed file that declares a benchmark class (a type declared in a file with `[Benchmark]`)
 *   selects it;
 * - a changed file that declares other types selects the benchmark classes that name them, through as
 *   many helper files as it takes. Names are matched as words, so a helper named in a comment selects
 *   too: that costs a class run, not a missed one;
 * - a baseline entry added, changed or removed selects the class it belongs to;
 * - anything in a suite's project that is not C# (the project file, the lock file), or a deleted C#
 *   file, selects the whole suite: what it reaches cannot be read from the sources;
 * - a change to the harness selects everything, since the harness is what the run proves.
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';

/** The files whose change is proven only by running every benchmark. */
export const HARNESS = [
  '.github/workflows/benchmarks.yml',
  'scripts/benchmarks.mjs',
  'scripts/benchmark-selection.mjs',
];

/** Where the allocation baseline lives, relative to a checkout. */
export const BASELINE_PATH = 'benchmarks/allocation-baseline.json';

const SKIPPED_DIRECTORIES = new Set(['bin', 'obj', 'BenchmarkDotNet.Artifacts']);

/** C# without its comments, so that a word in a comment does not read as a declaration. */
export function stripComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
}

/** The namespace of a C# file and the names of the types it declares. */
export function declarations(source) {
  const code = stripComments(source);
  const namespace = /\bnamespace\s+([\w.]+)/.exec(code)?.[1] ?? '';
  const types = new Set();
  for (const m of code.matchAll(/\b(?:class|struct|interface|enum|record)\s+(?:(?:class|struct)\s+)?([A-Za-z_]\w*)/g)) {
    // `where T : class` followed by another constraint.
    if (m[1] !== 'where') types.add(m[1]);
  }
  return { namespace, types: [...types], benchmark: /\[Benchmark\b/.test(code), code };
}

/**
 * The benchmark classes a change reaches, per suite.
 *
 * `head` and `base` map a path relative to the checkout ('/'-separated) to the file's text, and hold at
 * least the harness, the baseline and every file under each suite's project directory. Returns
 * `{ all: reason }` when everything must run, or `{ suites: { [name]: { all?: reason, classes: [] } } }`
 * where `classes` are full class names (`Namespace.Type`).
 */
export function selectBenchmarks(head, base, suites) {
  const changed = [...new Set([...head.keys(), ...base.keys()])].filter((p) => head.get(p) !== base.get(p)).sort();

  const harness = changed.find((p) => HARNESS.includes(p));
  if (harness) return { all: `${harness} changed` };

  const changedEntries = changedBaselineEntries(head.get(BASELINE_PATH), base.get(BASELINE_PATH));
  const result = {};
  for (const suite of suites) {
    const dir = dirname(suite.project).replaceAll('\\', '/') + '/';
    const inSuite = changed.filter((p) => p.startsWith(dir));
    const opaque = inSuite.find((p) => !p.endsWith('.cs') || !head.has(p));
    if (opaque) {
      result[suite.name] = { all: `${opaque} ${head.has(opaque) ? 'changed' : 'was deleted'}`, classes: [] };
      continue;
    }

    const files = [...head.keys()].filter((p) => p.startsWith(dir) && p.endsWith('.cs')).map((path) => ({ path, ...declarations(head.get(path)) }));
    const benchmarkClasses = new Map();
    for (const f of files.filter((f) => f.benchmark)) {
      for (const t of f.types) benchmarkClasses.set(t, f.namespace ? `${f.namespace}.${t}` : t);
    }
    const selected = new Set();

    // A partial benchmark class split over files without [Benchmark] is reached through any of them.
    const reached = new Set(files.filter((f) => inSuite.includes(f.path)).flatMap((f) => f.types));
    for (let grew = true; grew;) {
      grew = false;
      // A benchmark class is a leaf: nothing measured depends on it, so it does not spread.
      const helpers = [...reached].filter((t) => !benchmarkClasses.has(t));
      if (helpers.length === 0) break;
      const mentions = new RegExp(`\\b(?:${helpers.join('|')})\\b`);
      for (const f of files) {
        if (f.types.every((t) => reached.has(t)) || !mentions.test(f.code)) continue;
        for (const t of f.types) if (!reached.has(t)) (reached.add(t), (grew = true));
      }
    }
    for (const t of reached) if (benchmarkClasses.has(t)) selected.add(benchmarkClasses.get(t));

    for (const key of changedEntries) {
      for (const full of benchmarkClasses.values()) if (belongsTo(key, full)) selected.add(full);
    }
    result[suite.name] = { classes: [...selected].sort() };
  }
  return { suites: result };
}

/** The baseline keys added, removed or changed between two versions of the file. */
export function changedBaselineEntries(headText, baseText) {
  if (headText === baseText) return [];
  const parse = (text) => (text ? JSON.parse(text) : {});
  const [h, b] = [parse(headText), parse(baseText)];
  return [...new Set([...Object.keys(h), ...Object.keys(b)])].filter((k) => h[k] !== b[k]).sort();
}

/**
 * Whether a measurement or baseline key belongs to a class. A key starts with the report's name: the
 * class itself, or for a generic class the class and its type argument (`…Benchmarks_Twitter.Root_`).
 */
export function belongsTo(key, fullClassName) {
  return key.startsWith(`${fullClassName}.`) || key.startsWith(`${fullClassName}_`);
}

/** BenchmarkDotNet filters that run exactly these classes, generic or not. */
export function filtersFor(classes) {
  return classes.flatMap((c) => [`${c}.*`, `${c}<*`]);
}

/**
 * The classes in BenchmarkDotNet's `--list flat` output: one full benchmark name per line
 * (`Ns.Type.Method`, or `Ns.Type<Arg.Root>.Method` for a generic class), among the build's own lines.
 *
 * ⚠️ Only from the first column. A class the filter leaves empty makes BenchmarkDotNet print every
 * benchmark it has, indented, as a hint; read as a listing, that hint would select the whole suite.
 */
export function classesFromList(output) {
  const classes = new Set();
  for (const line of output.split(/\r?\n/)) {
    const name = line.trimEnd();
    if (!/^[A-Za-z_][\w.]*(<[\w.,\s]*>)?\.\w+$/.test(name)) continue;
    const withoutArguments = name.replace(/<[^>]*>/, '');
    classes.add(withoutArguments.slice(0, withoutArguments.lastIndexOf('.')));
  }
  return [...classes].sort();
}

/** The files `selectBenchmarks` reads, from a checkout on disk. */
export function readCheckout(checkout, suites) {
  const files = new Map();
  const add = (path) => {
    const full = join(checkout, path);
    if (existsSync(full)) files.set(path, readFileSync(full, 'utf8'));
  };
  for (const path of [...HARNESS, BASELINE_PATH]) add(path);

  const walk = (dir) => {
    if (!existsSync(dir)) return;
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED_DIRECTORIES.has(entry.name)) walk(full);
      } else {
        files.set(relative(checkout, full).replaceAll('\\', '/'), readFileSync(full, 'utf8'));
      }
    }
  };
  for (const suite of suites) walk(join(checkout, dirname(suite.project)));
  return files;
}
