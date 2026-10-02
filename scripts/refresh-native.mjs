#!/usr/bin/env node
/**
 * refresh-native.mjs — rebuilds the native libraries from the current source, stamped, and puts them in
 * their module's runtimes/. The only way a binary there passes the gate (see scripts/native-stamp.mjs):
 * each carries the hash of the source it was built from.
 *
 *   node scripts/refresh-native.mjs [pdf] [imaging]           the host's binary (win-x64 on Windows, linux-x64 on Linux)
 *   node scripts/refresh-native.mjs [pdf] [imaging] --linux   also linux-x64, built in a rust:1-bookworm container
 *
 * With no crate named, every crate. Each crate's unit tests run first: a binary is refreshed only from
 * source whose tests pass. osx-arm64 needs a macOS machine; nothing ships for it today.
 */
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { CRATES, ROOT, sourceHash, staleBinaries } from './native-stamp.mjs';

const args = process.argv.slice(2);
const linux = args.includes('--linux');
const named = args.filter((a) => !a.startsWith('--'));
const unknown = named.filter((n) => !CRATES.some((c) => c.name === n));
if (unknown.length) {
  console.error(`unknown crate: ${unknown.join(', ')} — known: ${CRATES.map((c) => c.name).join(', ')}`);
  process.exit(2);
}
const crates = named.length ? CRATES.filter((c) => named.includes(c.name)) : CRATES;

function step(title, cmd, cmdArgs, env) {
  console.log(`\n── ${title}\n   ${cmd} ${cmdArgs.join(' ')}`);
  const r = spawnSync(cmd, cmdArgs, { stdio: 'inherit', env: { ...process.env, ...env } });
  if (r.error || r.status !== 0) {
    console.error(`\nFAIL: ${title}${r.error ? ` (${r.error.message})` : ''}`);
    process.exit(1);
  }
}

function place(built, target) {
  mkdirSync(dirname(join(ROOT, target)), { recursive: true });
  copyFileSync(built, join(ROOT, target));
  console.log(`   → ${target}`);
}

const host = { win32: 'win-x64', linux: 'linux-x64' }[process.platform];

// The compiler writes source paths into the binary — panic locations, debug info — and on a developer's
// machine those are the user's home and the repository's place on disk. Mapped to neutral prefixes, so a
// shipped library names no machine. Encoded rather than RUSTFLAGS, which splits on spaces in a path.
const remap = [
  [process.env.CARGO_HOME ?? join(homedir(), '.cargo'), '/cargo'],
  [process.env.RUSTUP_HOME ?? join(homedir(), '.rustup'), '/rustup'],
  [ROOT, '/src'],
].map(([from, to]) => `--remap-path-prefix=${from}=${to}`);

for (const c of crates) {
  const hash = sourceHash(ROOT, c.sources);
  const env = { [c.env]: hash, CARGO_ENCODED_RUSTFLAGS: remap.join('\x1f') };
  const manifest = join(ROOT, c.dir, 'Cargo.toml');
  const target = (rid) => c.targets.find((t) => t.rid === rid);
  console.log(`\n${c.name}: source ${hash}`);

  step(`${c.name}: unit tests`, 'cargo', ['test', '--release', '--locked', '--manifest-path', manifest], env);

  if (host) {
    step(`${c.name}: build ${host}`, 'cargo', ['build', '--release', '--locked', '--manifest-path', manifest], env);
    place(join(ROOT, c.dir, 'target/release', target(host).built), target(host).path);
  }

  if (linux && host !== 'linux-x64') {
    // Read-only source, target and registry outside it: a Linux build never writes into the working tree.
    const out = join(tmpdir(), `pragmatic-${c.name}-linux-target`);
    mkdirSync(out, { recursive: true });
    step(`${c.name}: build linux-x64 (container)`, 'docker', [
      'run', '--rm',
      '-v', `${ROOT.replace(/\\/g, '/')}:/src:ro`,
      '-v', `${out.replace(/\\/g, '/')}:/target`,
      '-v', 'pragmatic-native-cargo-registry:/usr/local/cargo/registry',
      '-e', 'CARGO_TARGET_DIR=/target',
      '-e', `${c.env}=${hash}`,
      '-w', `/src/${c.dir}`,
      'rust:1-bookworm',
      'cargo', 'build', '--release', '--locked',
    ]);
    place(join(out, 'release', target('linux-x64').built), target('linux-x64').path);
  }
}

const stale = staleBinaries();
if (stale.length) {
  console.error('\nStill stale — the gate will refuse these:');
  for (const b of stale) console.error(`   ${b.path}${b.stamp ? '' : ' (no stamp)'}`);
  if (stale.some((b) => b.path.includes('/linux-x64/'))) console.error('   linux-x64: run again with --linux');
  process.exit(1);
}
console.log('\nEvery native binary is built from the current source.');
