#!/usr/bin/env node
/**
 * check-reachability.mjs — finds the types nobody uses.
 *
 * A review always looks from the inventory towards the code: what exists, is it documented, correct,
 * tested. The reverse direction is missing — *this type exists, who calls it?* — and it is where a
 * whole family of defects lives: something the framework declares, or the generator emits, that no
 * wiring connects.
 *
 * It is not an analyzer: it is an identifier count over the whole repository. Coarse on purpose — it
 * must run in a few seconds inside the gate and produce a list a person reads in a minute. The
 * calibrations below were measured in the field: without them the signal is unusable (538 candidates
 * instead of a dozen).
 *
 * Usage:
 *   node scripts/check-reachability.mjs                    the whole repository
 *   node scripts/check-reachability.mjs Pragmatic.Events   one module
 *   node scripts/check-reachability.mjs --json             for another tool
 */

import { readdir, readFile } from 'node:fs/promises';
import { join, relative, basename, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(import.meta.url), '..', '..');

/** Folders that hold no authored source. `generated` is NOT skipped: it is needed, see calibration 3. */
const SKIP_DIRS = new Set(['bin', 'node_modules', '.git', '.agentflow', 'artifacts', 'dist']);

const isGenerated = (p) => p.includes(`${sep}obj${sep}`) || p.endsWith('.g.cs');

/**
 * Calibration 5: ASP.NET's OpenAPI generator emits a cache of the XML comments of EVERY type in the
 * compilation, dead ones included. Counting it as a use makes everything alive and zeroes the signal.
 */
const isCommentCache = (p) => basename(p) === 'OpenApiXmlCommentSupport.generated.cs';

/** A type only its test knows is an orphan in production, which is the question that matters. */
const isTest = (p) => /[\\/]tests?[\\/]/i.test(p) || /Tests?\.cs$/.test(p);

/**
 * Comments are not uses. A type named in a comment that describes it as wired — exactly the defect —
 * would be acquitted if the mention counted as a reference.
 */
const stripComments = (s) =>
  s.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/.*$/gm, ' ');

async function* walk(dir) {
  let entries;
  try { entries = await readdir(dir, { withFileTypes: true }); } catch { return; }
  for (const e of entries) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      if (SKIP_DIRS.has(e.name)) continue;
      yield* walk(p);
    } else if (e.name.endsWith('.cs')) {
      yield p;
    }
  }
}

// Declaration of an authored type. Deliberately does not cover nested types: a nested type is reached
// through its container, so «zero references» on it means nothing.
const DECL = /^\s*(?:public|internal)\s+(?:(?:sealed|abstract|static|partial|readonly|record|ref)\s+)*(?:class|interface|record|struct|enum)\s+([A-Z][A-Za-z0-9_]*)/gm;

const TOKEN = /\b[A-Za-z_][A-Za-z0-9_]*\b/g;

/**
 * The calibrations measured in the field. Each removes a class of false positives that, left in,
 * buries the signal.
 */
/**
 * Calibration 6: Roslyn and MEF discover analyzers, generators and code fixers by reading an
 * attribute, not by naming the type. They have zero references by construction, and alone they were
 * half the candidates. The repository keeps one type per file (docs/CONVENTIONS.md), so looking for
 * the attribute in the file is as accurate as looking for it on the declaration.
 */
const DISCOVERY_ATTRS = /\[(?:DiagnosticAnalyzer|Generator|ExportCodeFixProvider|ExportCodeRefactoringProvider|DiagnosticSuppressor)\b|:\s*(?:IIncrementalGenerator|DiagnosticSuppressor)\b/;

function excludedBecause(name, file, discovered) {
  if (discovered) return 'discovered by attribute (analyzer, generator, code fixer)';
  // 1. Extension method classes are never named: the method is invoked on the instance.
  if (name.endsWith('Extensions')) return 'extension class — invoked by method, never by name';

  // 2. Convention constants are documented public API, not leftovers.
  if (name.endsWith('Tags') || name.endsWith('Conventions')) return 'documented public convention';

  // 3. The generator's templates and models are instantiated by the feature that owns them; if the
  //    count flags them it is because the name appears once, not because they are dead.
  if (/[\\/]Templates[\\/]|[\\/]Models[\\/]/.test(file) && /Template$|Model$/.test(name))
    return 'generator template or model';

  // 4. Attributes are generator input: the use is `[Foo]`, which the tokenizer sees as `Foo`, but an
  //    attribute declared and never applied is exactly what we want to see. No exclusion here —
  //    noted because it is the calibration one is tempted to add, and must not.
  return null;
}

const arg = process.argv.slice(2).find((a) => !a.startsWith('--'));
const asJson = process.argv.includes('--json');

const declarations = new Map(); // name -> { file }
const inProduction = new Map(); // authored code, tests and comments excluded
const inTests = new Map();
const inGenerated = new Map();

for await (const file of walk(ROOT)) {
  const rel = relative(ROOT, file);
  if (arg && !rel.startsWith(arg) && !isGenerated(file)) continue;
  if (isCommentCache(file)) continue;

  const raw = await readFile(file, 'utf8');
  const generated = isGenerated(file);

  // Declarations are collected only from authored source under src/.
  if (!generated && rel.includes(`${sep}src${sep}`)) {
    for (const m of raw.matchAll(DECL))
      if (!declarations.has(m[1]))
        declarations.set(m[1], { file: rel, discovered: DISCOVERY_ATTRS.test(raw) });
  }

  const target = generated ? inGenerated : isTest(rel) ? inTests : inProduction;
  const text = generated ? raw : stripComments(raw);
  for (const t of text.matchAll(TOKEN)) target.set(t[0], (target.get(t[0]) ?? 0) + 1);
}

const orphans = [];
const excluded = [];

for (const [name, info] of declarations) {
  const prod = inProduction.get(name) ?? 0;
  const gen = inGenerated.get(name) ?? 0;
  const tst = inTests.get(name) ?? 0;

  // Threshold ≤1 on production code: the declaration alone. Declaration plus one use is healthy —
  // with ≤2, nine of eleven sampled candidates were false positives.
  if (prod > 1) continue;

  // Calibration 3: a type that is generator input is referenced only from the .g.cs.
  if (gen > 0) continue;

  const why = excludedBecause(name, info.file, info.discovered);
  if (why) { excluded.push({ name, file: info.file, why }); continue; }

  orphans.push({ name, file: info.file, tests: tst });
}

// First the ones with a test suite and no caller: the exact signature of the family — someone wrote
// it and tested it, and then did not connect it.
orphans.sort((a, b) => (b.tests > 0) - (a.tests > 0) || a.file.localeCompare(b.file));

if (asJson) {
  console.log(JSON.stringify({ orphans, excludedCount: excluded.length }, null, 2));
  process.exit(0);
}

const scope = arg ? ` in ${arg}` : '';
console.log(`${declarations.size} types declared${scope} · ${excluded.length} excluded by the calibrations\n`);

if (orphans.length === 0) {
  console.log('No orphan types.');
  process.exit(0);
}

console.log(`${orphans.length} declared types that nothing else in the repository names:\n`);
let lastArea = '';
for (const o of orphans) {
  const area = o.file.split(sep).slice(0, 2).join('/');
  if (area !== lastArea) { console.log(`  ${area}`); lastArea = area; }
  console.log(`    ${o.name.padEnd(42)} ${basename(o.file)}`);
}

console.log(`
A type here is not necessarily dead: it may be an extension point offered to whoever builds on top.
It is a question, not a verdict — but it is the question a review does not ask.`);
