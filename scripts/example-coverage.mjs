/**
 * What the example applications cover, attribute by attribute.
 *
 *   node scripts/example-coverage.mjs           the summary and every module below 100%
 *   node scripts/example-coverage.mjs --all     every module
 *
 * The gate carries the number (`exampleCoverageRatchet()` in check.mjs); this prints the names, which
 * is what a story about a gap needs. The measure, and the four ways it was wrong before it lived in
 * `scripts/lib/example-coverage.mjs`, are documented there.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { measureCoverage } from './lib/example-coverage.mjs';
import {
  COVERAGE_REASONS, PACKAGE_REASONS, reasonsWithMissingProofs,
} from './lib/example-coverage-reasons.mjs';

const all = process.argv.includes('--all');
const m = measureCoverage({ repoRoot: '.' });

/**
 * Every test source, read once.
 *
 * ⚠️ The filesystem, not `git grep`. Measured while writing this: `AMiddlewareScopedToOneMessageTypeTests`
 * is not tracked, so git reported it missing and the register raised a false alarm about a test that
 * is right there. In a tree that carries uncommitted work — this one does, for days at a time — asking
 * git answers "is it committed", and the question is "does it exist".
 */
function readTestSources(dir, into) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin' || entry.name === 'node_modules'
      || entry.name === 'artifacts' || entry.name === '.git')
      continue;

    const path = join(dir, entry.name);
    if (entry.isDirectory()) readTestSources(path, into);
    else if (entry.name.endsWith('Tests.cs') || entry.name.endsWith('Suite.cs'))
      into.push(readFileSync(path, 'utf8'));
  }

  return into;
}

const testSources = readTestSources('.', []);

/** Whether a test name appears in a test source — the check that keeps a reason from outliving its proof. */
const existsInTests = name => testSources.some(source => source.includes(name));

const pct = (n, total) => `${Math.round((100 * n) / total)}%`;

console.log(`attributes ${m.total}   written by an example ${m.byExample} (${pct(m.byExample, m.total)})`
  + `   written by the generator ${m.byGenerator}   written by nobody ${m.unwritten}`);

if (!m.generatedOutputSeen)
  console.log('  ⚠️  no generated output under examples/**/obj — build the examples, or the generator'
    + '\n      bucket reads 0 and its attributes are counted as gaps.');

console.log();

for (const r of [...m.rows].sort((a, b) => a.byExample.length / a.total - b.byExample.length / b.total
  || b.total - a.total)) {
  if (!all && r.unwritten.length === 0) continue;
  console.log(`${pct(r.byExample.length, r.total).padStart(4)}  ${r.module.padEnd(38)} `
    + `${r.byExample.length}/${r.total}`);
  if (r.unwritten.length > 0) {
    // Split the bucket: a name with a recorded reason is a decision, the rest are gaps. Printing
    // them in one list is how a shortfall somebody already answered gets answered again, by bending
    // an example — which is the outcome the register exists to prevent.
    const decided = r.unwritten.filter(a => COVERAGE_REASONS[a]);
    const gaps = r.unwritten.filter(a => !COVERAGE_REASONS[a]);

    if (gaps.length > 0) console.log(`        nobody:    ${gaps.join(' ')}`);
    for (const attribute of decided) {
      console.log(`        decided:   ${attribute} — ${COVERAGE_REASONS[attribute].why}`);
      console.log(`                   proved by: ${COVERAGE_REASONS[attribute].provedBy.join(', ')}`);
    }
  }
  if (r.byGenerator.length > 0) console.log(`        generator: ${r.byGenerator.join(' ')}`);
}

// Packages carry no attribute, so the table above cannot hold them; the same decision applies and it
// has to be readable in the same place.
const packages = Object.entries(PACKAGE_REASONS);
if (packages.length > 0) {
  console.log('\npackages an example does not use, and why');
  for (const [name, entry] of packages) {
    console.log(`  ${name} — ${entry.why}`);
    console.log(`    proved by: ${entry.provedBy.join(', ')}`);
  }
}

// ⚠️ A reason outliving its proof is the failure this register would otherwise introduce.
const missing = reasonsWithMissingProofs(existsInTests);
if (missing.length > 0) {
  console.log('\n⚠️  a recorded reason names a proof that is not in the repository:');
  for (const { attribute, proof } of missing)
    console.log(`  ${attribute} → ${proof}`);
  process.exitCode = 1;
}
