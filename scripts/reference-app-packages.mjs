/**
 * Which runtime packages the reference applications reach, following ProjectReference transitively.
 *
 *   node scripts/reference-app-packages.mjs                              every reference application
 *   node scripts/reference-app-packages.mjs time-off invoicing casework  only these (a "before")
 *
 * A runtime package is a project under `Pragmatic.*\src\` that is not a generator, analyzer, code fixer
 * or command-line tool. The Showcase is not a reference application — it is a shop window — so a
 * package only it reaches is listed apart: reached, but by nothing that is built like an application.
 *
 * The count is of what an application *references*, not of what it exercises: a package reached through
 * another's reference counts as reached. Enough to see what an example moved, and what none touches.
 */
import { readFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import { join, resolve, dirname, basename } from 'node:path';

const REFERENCE_APPS = ['time-off', 'invoicing', 'casework', 'warehouse'];
const root = resolve('.');
const apps = process.argv.slice(2).length > 0 ? process.argv.slice(2) : REFERENCE_APPS;

function csprojs(dir, into = []) {
  for (const entry of readdirSync(dir)) {
    if (['bin', 'obj', 'node_modules', '.git', 'artifacts'].includes(entry)) continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) csprojs(path, into);
    else if (entry.endsWith('.csproj')) into.push(path);
  }
  return into;
}

function references(project) {
  return [...readFileSync(project, 'utf8').matchAll(/ProjectReference\s+Include="([^"]+)"/g)]
    .map(match => resolve(dirname(project), match[1].replace(/\\/g, '/')))
    .filter(existsSync);
}

function reachedFrom(projects) {
  const seen = new Set();
  const stack = [...projects];
  while (stack.length) {
    const project = stack.pop();
    if (seen.has(project)) continue;
    seen.add(project);
    stack.push(...references(project));
  }
  return new Set([...seen].map(project => basename(project, '.csproj')));
}

const runtime = [...new Set(csprojs(root)
  .filter(path => /[\\/]Pragmatic\.[^\\/]+[\\/]src[\\/]/.test(path))
  .map(path => basename(path, '.csproj'))
  .filter(name => !/SourceGenerator|Analyzers|CodeFixers|Generator$|\.Cli$/.test(name)))].sort();

for (const app of apps)
  if (!existsSync(join(root, 'examples', app)))
    throw new Error(`No example '${app}' under examples/.`);

const byApps = reachedFrom(apps.flatMap(app => csprojs(join(root, 'examples', app))));
const byShowcase = reachedFrom(csprojs(join(root, 'examples', 'showcase')));
const reached = runtime.filter(name => byApps.has(name));
const missing = runtime.filter(name => !byApps.has(name));

console.log(`reference applications: ${apps.join(', ')}`);
console.log(`runtime packages ${runtime.length}, reached by a reference application ${reached.length}, by none ${missing.length}`);
console.log('\nreached only by the Showcase:');
for (const name of missing.filter(n => byShowcase.has(n))) console.log(`  ${name}`);
console.log('\nreached by no example at all:');
for (const name of missing.filter(n => !byShowcase.has(n))) console.log(`  ${name}`);
