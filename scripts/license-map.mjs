#!/usr/bin/env node
/**
 * Every module is named in docs/LICENSING.md, the page that says which license it ships under.
 *
 * Directory.Build.props gives a module MIT when its path is on the free list and PolyForm otherwise, so a
 * module nobody placed ships under PolyForm without anyone having decided it. Checking the page against
 * the module folders is what surfaces such a module.
 *
 *   node scripts/license-map.mjs [--root <dir>]   exits 1 naming each module the page does not mention
 */
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const rootArg = process.argv.indexOf('--root');
const root = rootArg > 0 ? resolve(process.argv[rootArg + 1]) : join(dirname(fileURLToPath(import.meta.url)), '..');

const page = join(root, 'docs', 'LICENSING.md');
if (!existsSync(page)) {
  console.log('docs/LICENSING.md is missing: no module has a declared license.');
  process.exit(1);
}
const licensing = readFileSync(page, 'utf8');

// A module is a Pragmatic.* folder that ships something: it has a src/ folder.
const modules = readdirSync(root)
  .filter((name) => name.startsWith('Pragmatic.') && statSync(join(root, name)).isDirectory())
  .filter((name) => existsSync(join(root, name, 'src')));

const unplaced = modules.filter((name) => !licensing.includes(`\`${name}\``));

if (unplaced.length > 0) {
  console.log(`modules with no declared license — ${unplaced.length}:`);
  for (const name of unplaced) console.log(`  ${name}`);
  console.log('  Name each in docs/LICENSING.md under its bucket (and on the free list in Directory.Build.props if MIT).');
  process.exit(1);
}

console.log(`every module has a declared license (${modules.length} modules)`);
