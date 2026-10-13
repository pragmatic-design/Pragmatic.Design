#!/usr/bin/env node
/**
 * Whether a change needs the build in CI, from the paths it touches (#199).
 *
 * `docs` when every path is Markdown that nothing compiles, packs or syncs: the gate then runs
 * `--tier docs` (the checks that read sources only) and the jobs that build skip their work. `full`
 * otherwise, and whenever there is any doubt. The exceptions are Markdown that the build reads:
 * - the root README.md (the package README of a module without its own) and a module's README.md and
 *   docs/, which are packed into its packages, where Pack & Validate checks them;
 * - anything under marketplace/, since a skill's examples are synced from compiled sources.
 *
 *   node scripts/ci-paths.mjs --base <sha>   # prints the scope, and writes scope=… to $GITHUB_OUTPUT
 *
 * Without --base (a push to main, a release, a manual run) the scope is `full`.
 */
import { execFileSync, spawnSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Markdown that no build, pack or sync reads. */
function needsNoBuild(path) {
  const p = path.replace(/\\/g, '/');
  if (!p.toLowerCase().endsWith('.md')) return false;
  if (p.startsWith('marketplace/')) return false;
  if (p === 'README.md') return false;
  if (/^Pragmatic\.[^/]+\/README\.md$/.test(p)) return false;
  if (/^Pragmatic\.[^/]+\/docs\//.test(p)) return false;
  return true;
}

/** `docs` when every changed path needs no build, `full` otherwise; no paths is `full`. */
export function scopeOf(paths) {
  return paths.length > 0 && paths.every(needsNoBuild) ? 'docs' : 'full';
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
    for (const p of paths) console.log(`  ${needsNoBuild(p) ? 'docs' : 'full'}  ${p}`);
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
