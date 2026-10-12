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

/**
 * The namespace of a C# file, the names of the types it declares, and the classes each class derives
 * from or implements (`bases`, by simple name: `Ns.Base<T>` reads as `Base`).
 */
export function declarations(source) {
  const code = stripComments(source);
  const namespace = /\bnamespace\s+([\w.]+)/.exec(code)?.[1] ?? '';
  const types = new Set();
  for (const m of code.matchAll(/\b(?:class|struct|interface|enum|record)\s+(?:(?:class|struct)\s+)?([A-Za-z_]\w*)/g)) {
    // `where T : class` followed by another constraint.
    if (m[1] !== 'where') types.add(m[1]);
  }

  // A class header up to its base list: name, type parameters, a primary constructor, then `:`. The list
  // ends at the body, at the `;` of a class with none, or at its constraints.
  const bases = {};
  for (const m of code.matchAll(/\bclass\s+([A-Za-z_]\w*)\s*(?:<[^>{;:]*>)?\s*(?:\([^)]*\))?\s*:\s*([^{;]+?)\s*(?:\bwhere\b|[{;])/g)) {
    bases[m[1]] = splitTopLevel(m[2]).map((b) => b.replace(/<.*$/s, '').trim().split('.').pop()).filter(Boolean);
  }

  return { namespace, types: [...types], bases, benchmark: /\[Benchmark\b/.test(code), code };
}

/** A base list split at its top-level commas: `Base<A, B>, IFoo` is two entries, not three. */
function splitTopLevel(list) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < list.length; i++) {
    if (list[i] === '<') depth++;
    else if (list[i] === '>') depth--;
    else if (list[i] === ',' && depth === 0) {
      parts.push(list.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(list.slice(start));
  return parts;
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

    // A class that derives from a benchmark class runs its benchmarks, and its own file need not hold a
    // [Benchmark] (#215): rows in an abstract class, run by one derived class per job. Closed over as
    // many derivations as there are.
    const derived = new Map();
    for (const f of files) {
      for (const [t, bases] of Object.entries(f.bases)) {
        for (const b of bases) derived.set(b, [...(derived.get(b) ?? []), [t, f.namespace]]);
      }
    }
    for (let grew = true; grew;) {
      grew = false;
      for (const name of [...benchmarkClasses.keys()]) {
        for (const [t, namespace] of derived.get(name) ?? []) {
          if (benchmarkClasses.has(t)) continue;
          benchmarkClasses.set(t, namespace ? `${namespace}.${t}` : t);
          grew = true;
        }
      }
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
    // A selected class carries the classes that derive from it: they run what it declares.
    const inheriting = [...reached].filter((t) => benchmarkClasses.has(t));
    for (let i = 0; i < inheriting.length; i++) {
      for (const [t] of derived.get(inheriting[i]) ?? []) if (!inheriting.includes(t)) inheriting.push(t);
    }
    for (const t of inheriting) selected.add(benchmarkClasses.get(t));

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
