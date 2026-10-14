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
 * - the modules a suite's project references, transitively, are read the same way: a changed type in
 *   one selects the benchmark classes that use it, through the helpers in the module and in the
 *   benchmark project alike (#189);
 * - anything in a suite's project or a module it references that is not C# (a project file, a lock
 *   file), or a deleted C# file, selects the whole suite: what it reaches cannot be read from the sources;
 * - a change to a generator the suite is built with, or to the shared code compiled into every
 *   generator, selects the whole suite: it changes generated code, which no benchmark names;
 * - a change to the harness, or to a file every project is built with, selects everything.
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, posix, relative } from 'node:path';

/** The files whose change is proven only by running every benchmark. */
export const HARNESS = [
  '.github/workflows/benchmarks.yml',
  'scripts/benchmarks.mjs',
  'scripts/benchmark-selection.mjs',
];

/** Files outside any project that every project is built with: the SDK, the build and the packages. */
export const BUILD_INPUTS = [
  'Directory.Build.props',
  'Directory.Build.targets',
  'Directory.Packages.props',
  'global.json',
];

/**
 * The shared code `Directory.Build.props` compiles into every generator (`IsGenerator`), without its
 * Testing folder, which only generator tests get.
 */
export const GENERATOR_SHARED_SOURCE = 'shared/SourceGen/';
const GENERATOR_SHARED_TESTING = 'shared/SourceGen/Testing/';

/** The MSBuild properties a project file uses in a path, as `Directory.Build.props` defines them. */
const PATH_PROPERTIES = { '$(PragmaticRoot)': '', '$(SharedSourceGenPath)': GENERATOR_SHARED_SOURCE };

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
 * The projects a benchmark project is built from: itself and its `ProjectReference`s, transitively, by
 * checkout-relative path. Each has its directory (`dir`, with a trailing '/'), the files outside it that
 * it compiles by name (`compiled`), and whether it is `opaque`: referenced as an analyzer (a generator),
 * or referenced by one. `unresolved` names each path an MSBuild property hides from this reading.
 *
 * `read` gives a project file's text by checkout-relative path, or undefined when there is none.
 */
export function referencedProjects(read, project) {
  const projects = new Map();
  const unresolved = [];
  const visit = (path, opaque) => {
    const known = projects.get(path);
    if (known && (known.opaque || !opaque)) return;
    const text = read(path);
    if (text === undefined) return;
    const dir = posix.dirname(path);
    const items = projectItems(text, dir);
    projects.set(path, { dir: `${dir}/`, compiled: items.compiled, opaque });
    for (const include of items.hidden) unresolved.push({ project: path, include });
    for (const r of items.references) visit(r.path, opaque || r.analyzer);
  };
  visit(project.replaceAll('\\', '/'), false);
  return { projects, unresolved };
}

/** A project file's references and named sources, as checkout-relative paths. */
function projectItems(text, dir) {
  const references = [];
  const compiled = [];
  const hidden = [];
  for (const m of text.replace(/<!--[\s\S]*?-->/g, '').matchAll(/<(ProjectReference|Compile)\b([^>]*)>/g)) {
    // `<Compile Remove="…">` and `Update` name nothing the project gains.
    const include = /\bInclude="([^"]+)"/.exec(m[2])?.[1];
    if (!include) continue;
    const path = resolveInclude(include, dir);
    if (path === null) hidden.push(include);
    else if (m[1] === 'ProjectReference') references.push({ path, analyzer: /\bOutputItemType="Analyzer"/.test(m[2]) });
    else compiled.push(path);
  }
  return { references, compiled, hidden };
}

/** An `Include` as a checkout-relative path, or null when a property or a wildcard hides what it names. */
function resolveInclude(include, dir) {
  let path = include.replaceAll('\\', '/');
  const property = Object.keys(PATH_PROPERTIES).find((p) => path.startsWith(p));
  if (property) path = PATH_PROPERTIES[property] + path.slice(property.length);
  else path = posix.join(dir, path);
  return path.includes('$(') || path.includes('*') ? null : posix.normalize(path);
}

/**
 * The benchmark classes a change reaches, per suite.
 *
 * `head` and `base` map a path relative to the checkout ('/'-separated) to the file's text, and hold at
 * least what `readCheckout` reads: the harness, the build inputs, the baseline, and every file of each
 * project a suite is built from. Returns `{ all: reason }` when everything must run, or
 * `{ suites: { [name]: { all?: reason, classes: [] } } }` where `classes` are full class names
 * (`Namespace.Type`).
 */
export function selectBenchmarks(head, base, suites) {
  const changed = [...new Set([...head.keys(), ...base.keys()])].filter((p) => head.get(p) !== base.get(p)).sort();

  const harness = changed.find((p) => HARNESS.includes(p) || BUILD_INPUTS.includes(p));
  if (harness) return { all: `${harness} changed` };

  const changedEntries = changedBaselineEntries(head.get(BASELINE_PATH), base.get(BASELINE_PATH));
  const how = (p) => `${p} ${head.has(p) ? 'changed' : 'was deleted'}`;
  const result = {};
  for (const suite of suites) {
    const dir = dirname(suite.project).replaceAll('\\', '/') + '/';
    const { projects, unresolved } = referencedProjects((p) => head.get(p) ?? base.get(p), suite.project);
    const of = (opaque) => {
      const chosen = [...projects.values()].filter((p) => p.opaque === opaque);
      return (path) => chosen.some((p) => path.startsWith(p.dir) || p.compiled.includes(path));
    };
    const inPlain = of(false);
    const inOpaque = of(true);
    const generated = [...projects.values()].some((p) => p.opaque);
    const inGenerator = (p) => inOpaque(p)
      || (generated && p.startsWith(GENERATOR_SHARED_SOURCE) && !p.startsWith(GENERATOR_SHARED_TESTING));

    const inSuite = changed.filter(inPlain);
    const opaque = changed.find(inGenerator) ?? inSuite.find((p) => !p.endsWith('.cs') || !head.has(p));
    if (opaque) {
      result[suite.name] = { all: how(opaque), classes: [] };
      continue;
    }
    if (unresolved.length > 0) {
      const { project, include } = unresolved[0];
      result[suite.name] = { all: `${project} references ${include}, which the selection cannot resolve`, classes: [] };
      continue;
    }

    const files = [...head.keys()].filter((p) => p.endsWith('.cs') && inPlain(p)).map((path) => ({ path, ...declarations(head.get(path)) }));
    const benchmarkClasses = new Map();
    for (const f of files.filter((f) => f.benchmark && f.path.startsWith(dir))) {
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
  for (const path of [...HARNESS, ...BUILD_INPUTS, BASELINE_PATH]) add(path);

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
  // Only the benchmark project's own folder was read, so a change to a module's source reached nothing (#189).
  for (const suite of suites) {
    const read = (path) => (add(path), files.get(path));
    const { projects } = referencedProjects(read, suite.project);
    for (const project of projects.values()) {
      walk(join(checkout, project.dir));
      for (const path of project.compiled) add(path);
    }
    if ([...projects.values()].some((p) => p.opaque)) walk(join(checkout, GENERATOR_SHARED_SOURCE));
  }
  return files;
}
