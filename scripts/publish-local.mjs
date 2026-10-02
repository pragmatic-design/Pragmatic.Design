#!/usr/bin/env node
// Builds, packs and pushes every package to the local BaGetter feed.
//
// One command instead of four, and that is the point beyond convenience: the manual sequence had two
// silent failure modes. A `dotnet pack` without a preceding clean `--no-incremental` build publishes
// stale binaries, and a `node scripts/check.mjs | tail` run from the wrong directory reports the exit
// code of `tail` — a step that never ran, announcing success. Here the cwd is derived from this file
// and every step's exit code is checked.
//
//   node scripts/publish-local.mjs            → next version after the highest on the feed
//   node scripts/publish-local.mjs 3958       → that exact build number
//   node scripts/publish-local.mjs --no-build → pack + push only, when the Release build is current
//
// After pushing it clears NuGet's HTTP cache: without that, a consumer restores the previous version
// from cache and the whole verification is done against the wrong bits.

import { execFileSync, execSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SOLUTION = 'Pragmatic.Design.slnx';
const FEED = 'http://localhost:5555/v3/index.json';
const API_KEY = 'dev-key';
/**
 * The config `dotnet nuget push` is given, written into this repository's artifacts when it is not
 * there.
 *
 * ⚠️ Written by this script, inside the repository: a config at a path outside it, created by nothing
 * and documented nowhere, makes the first command the README points a new contributor at fail on a
 * file they have no way to know about. It lives under artifacts/ rather than at the root on purpose: a root NuGet.Config changes how every project here restores,
 * and this file exists only so a push has somewhere to read the source from.
 */
const NUGET_CONFIG = join(REPO, 'artifacts', 'local-feed.nuget.config');
const PREFIX = '1.0.0-alpha.0.';
const PROBE_PACKAGE = 'pragmatic.abstractions';

const args = process.argv.slice(2);
const skipBuild = args.includes('--no-build');
const explicit = args.find((a) => /^\d+$/.test(a));

const green = (s) => `\u001b[32m${s}\u001b[0m`;
const red = (s) => `\u001b[31m${s}\u001b[0m`;
const dim = (s) => `\u001b[2m${s}\u001b[0m`;

function run(file, argv, label) {
  process.stdout.write(`  ${label} … `);
  try {
    execFileSync(file, argv, { cwd: REPO, stdio: 'pipe' });
    console.log(green('ok'));
  } catch (error) {
    console.log(red('FAIL'));
    const output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
    // Only the lines that say why: a full MSBuild log buries the error under a thousand warnings.
    const errors = output.split(/\r?\n/).filter((l) => /error|Errori\s*:\s*[1-9]/i.test(l));
    console.error((errors.length ? errors : output.split(/\r?\n/)).slice(-15).join('\n'));
    process.exit(1);
  }
}

/** The next build number, read from the feed rather than guessed. */
async function nextVersion() {
  if (explicit) return PREFIX + explicit;

  let response;
  try {
    response = await fetch(`http://localhost:5555/v3/package/${PROBE_PACKAGE}/index.json`);
  } catch (cause) {
    throw new Error('the feed is not answering at http://localhost:5555 — is BaGetter up?', { cause });
  }

  // ⚠️ A feed that has never received this package answers 404, and that is the FIRST publish — the
  // one case the probe exists to serve, which must not be refused with "is BaGetter up?" on a feed
  // that is up. Unreachable is the throw above; not-found starts the numbering.
  if (response.status === 404) return `${PREFIX}1`;

  if (!response.ok) throw new Error(`the feed answered ${response.status} for ${PROBE_PACKAGE}`);

  const { versions } = await response.json();
  const highest = versions
    .filter((v) => v.startsWith(PREFIX))
    .map((v) => Number(v.slice(PREFIX.length)))
    .reduce((a, b) => Math.max(a, b), 0);

  return PREFIX + (highest + 1);
}

/**
 * Writes the push config if it is not there, so a clean clone needs nothing prepared by hand.
 *
 * Only the source and its key: `allowInsecureConnections` because the local feed is plain HTTP, which
 * is also why nothing here belongs outside a developer machine.
 */
function ensurePushConfig() {
  if (existsSync(NUGET_CONFIG)) return;

  mkdirSync(dirname(NUGET_CONFIG), { recursive: true });
  writeFileSync(NUGET_CONFIG, `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-bagetter" value="${FEED}" allowInsecureConnections="true" />
  </packageSources>
</configuration>
`);
  console.log(`  ${dim(`wrote ${NUGET_CONFIG}`)}`);
}

ensurePushConfig();

const version = await nextVersion();
const output = join(REPO, 'artifacts', 'local-feed', version);

console.log(`\npublishing ${green(version)} ${dim(`→ ${FEED}`)}\n`);

if (!skipBuild) {
  // --no-incremental deliberately: MSBuild does not re-evaluate projects it considers up to date, and
  // a build that reports zero errors on sources that fail when compiled clean has happened here before.
  run('dotnet', ['build', SOLUTION, '-c', 'Release', '--no-incremental',
    `-p:MinVerVersionOverride=${version}`, '-v', 'q', '--nologo'], 'clean Release build');
}

rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });

run('dotnet', ['pack', SOLUTION, '-c', 'Release', '--no-build',
  `-p:MinVerVersionOverride=${version}`, '-o', output, '-v', 'q', '--nologo'], 'pack');

const packages = readdirSync(output).filter((f) => f.endsWith('.nupkg'));
if (packages.length === 0) {
  console.error(red('  pack produced no .nupkg — nothing to push'));
  process.exit(1);
}

const stale = packages.filter((f) => !f.includes(version));
if (stale.length > 0) {
  console.error(red(`  ${stale.length} package(s) do not carry ${version}: ${stale[0]}`));
  process.exit(1);
}

// A consumer reads the README and the docs from the extracted package: a link that resolves only in
// the repository is dead there, so a package carrying one does not reach the feed.
run(process.execPath, [join(REPO, 'scripts', 'package-markdown-links.mjs'), output], 'markdown links resolve inside each package');

process.stdout.write(`  push ${packages.length} packages … `);
let pushed = 0;
const failed = [];
for (const pkg of packages) {
  try {
    execFileSync('dotnet', ['nuget', 'push', join(output, pkg), '-s', FEED, '-k', API_KEY,
      '--configfile', NUGET_CONFIG], { cwd: REPO, stdio: 'pipe' });
    pushed++;
  } catch {
    failed.push(pkg);
  }
}

if (failed.length > 0) {
  console.log(red(`FAIL (${pushed} pushed, ${failed.length} failed)`));
  failed.slice(0, 5).forEach((f) => console.error(`    ${f}`));
  process.exit(1);
}
console.log(green(`ok`) + dim(` (${pushed})`));

// Without this a consumer restores the previous version from cache and verifies the wrong bits.
execSync('dotnet nuget locals http-cache --clear', { stdio: 'pipe' });
console.log(`  ${green('ok')} http cache cleared`);

console.log(`\n${green('PUBLISHED')} ${version}\n`);
