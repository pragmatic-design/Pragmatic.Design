#!/usr/bin/env node
/**
 * What a change needs in CI, from the paths it touches (#199). Three scopes, the widest path deciding:
 *
 * - `docs`: Markdown that nothing compiles, packs or syncs, and anything under site/, which no .NET project
 *   reads (docs.yml builds the site, and the docs tier checks the pages synced into it). The gate runs
 *   `--tier docs` (the checks that read sources only) and every job that builds skips its work.
 * - `pack`: Markdown packed into the packages, the root README.md (the package README of a module without
 *   its own) and a module's README.md and docs/. Pack & Validate builds and checks them; the gate runs
 *   `--tier docs`, and the container suites skip, since no test reads them.
 * - `full`: everything else, and whenever there is any doubt. Anything under marketplace/ is `full`, since a
 *   skill's examples are synced from compiled sources.
 *
 *   node scripts/ci-paths.mjs --base <sha>   # prints the scope, and writes scope=… to $GITHUB_OUTPUT
 *
 * Without --base (a push to main, a release, a manual run) the scope is `full`.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WIDTH = { docs: 0, pack: 1, full: 2 };

/** The scope one path needs on its own. */
function scopeOfPath(path) {
  const p = path.replace(/\\/g, '/');
  if (p.startsWith('site/')) return 'docs';
  if (!p.toLowerCase().endsWith('.md')) return 'full';
  if (p.startsWith('marketplace/')) return 'full';
  if (p === 'README.md') return 'pack';
  if (/^Pragmatic\.[^/]+\/README\.md$/.test(p)) return 'pack';
  if (/^Pragmatic\.[^/]+\/docs\//.test(p)) return 'pack';
  return 'docs';
}

/** The widest scope any changed path needs; no paths is `full`. */
export function scopeOf(paths) {
  if (paths.length === 0) return 'full';
  return paths.map(scopeOfPath).reduce((widest, scope) => (WIDTH[scope] > WIDTH[widest] ? scope : widest), 'docs');
}

function changedSince(base) {
  // A two-dot diff of two trees needs the base commit and no merge base. A shallow checkout (the gate,
  // the shards) fetches it alone. A full one (Pack & Validate, for MinVer's history) must never be told
  // --depth, which would cut the history MinVer reads next.
  const have = spawnSync('git', ['cat-file', '-e', `${base}^{commit}`]).status === 0;
  if (!have) {
    const shallow = execFileSync('git', ['rev-parse', '--is-shallow-repository'], { encoding: 'utf8' }).trim() === 'true';
    execFileSync('git', ['fetch', '--no-tags', ...(shallow ? ['--depth=1'] : []), 'origin', base], { stdio: 'inherit' });
  }
  return execFileSync('git', ['diff', '--name-only', base, 'HEAD'], { encoding: 'utf8' })
    .split('\n').map((l) => l.trim()).filter(Boolean);
}

function main(argv) {
  const at = argv.indexOf('--base');
  const base = at >= 0 ? argv[at + 1] : '';
  let scope = 'full';
  if (base) {
    const paths = changedSince(base);
    scope = scopeOf(paths);
    console.log(`${paths.length} path(s) changed since ${base.slice(0, 12)}:`);
    for (const p of paths) console.log(`  ${scopeOfPath(p)}  ${p}`);
  } else {
    console.log('no base to compare with: full');
  }
  console.log(`scope: ${scope}`);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `scope=${scope}\n`);
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(main(process.argv.slice(2)));
}
