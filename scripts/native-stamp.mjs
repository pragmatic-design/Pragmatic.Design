#!/usr/bin/env node
/**
 * native-stamp.mjs — ties each committed native binary to the Rust source it comes from.
 *
 * The binaries in Pragmatic.Documents/runtimes/ and Pragmatic.Imaging/runtimes/ are built outside the
 * .NET build and copied in. The PDF one on Windows once stayed behind its source for five months:
 * 1668ea4cd changed the engine three hours after the DLL was committed, the DLL was never rebuilt, and
 * every test stayed green.
 *
 * So each binary carries a stamp: the hash of its crate's source, written into it at build time (each
 * crate's build.rs reads its env variable, e.g. PRAGMATIC_PDF_SOURCE_HASH). The gate hashes the source
 * again and reads the stamp out of each binary's bytes, which works for every platform's binary on any
 * machine. A binary built from other source, or built without the stamp, fails the gate.
 *
 *   node scripts/native-stamp.mjs hash <crate>   the hash of that crate's current source
 *   node scripts/native-stamp.mjs check          exit 1, naming each stale binary
 *   node scripts/native-stamp.mjs verify <crate> <file>   exit 1 unless that built library carries the
 *                                                         current source's stamp (the CI workflows)
 *
 * The fix is always the same command: node scripts/refresh-native.mjs
 */
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = resolve(fileURLToPath(new URL('..', import.meta.url)));

/** Both crates build against it: a change here makes every binary stale. */
const SHARED = ['shared/native/pragmatic-core/Cargo.toml', 'shared/native/pragmatic-core/src'];

function crate(name, dir, env, lib, runtimes, dllName) {
  return {
    name,
    dir,
    env,
    /** The CI workflow that builds every RID, osx-arm64 included — the only source of that one. */
    workflow: `.github/workflows/${name}-native.yml`,
    sources: [`${dir}/Cargo.toml`, `${dir}/Cargo.lock`, `${dir}/build.rs`, `${dir}/src`, ...SHARED],
    /** Where each RID's binary is committed, and the file cargo builds for it. */
    targets: [
      { rid: 'win-x64', path: `${runtimes}/win-x64/native/${dllName}`, built: `${lib}.dll` },
      { rid: 'linux-x64', path: `${runtimes}/linux-x64/native/lib${lib}.so`, built: `lib${lib}.so` },
      { rid: 'osx-arm64', path: `${runtimes}/osx-arm64/native/lib${lib}.dylib`, built: `lib${lib}.dylib` },
    ],
    get binaries() { return this.targets.map((t) => t.path); },
  };
}

export const CRATES = [
  crate('pdf', 'Pragmatic.Documents/native/pragmatic-pdf', 'PRAGMATIC_PDF_SOURCE_HASH',
    'pragmatic_pdf_native', 'Pragmatic.Documents/runtimes', 'Pragmatic.Pdf.Native.dll'),
  crate('imaging', 'Pragmatic.Imaging/native/pragmatic-imaging', 'PRAGMATIC_IMAGING_SOURCE_HASH',
    'pragmatic_imaging_native', 'Pragmatic.Imaging/runtimes', 'Pragmatic.Imaging.Native.dll'),
];

function filesUnder(root, path) {
  const full = join(root, path);
  if (!existsSync(full)) return [];
  if (!statSync(full).isDirectory()) return [path];
  return readdirSync(full).flatMap((name) => filesUnder(root, `${path}/${name}`));
}

/**
 * The hash of the source, independent of the checkout: paths are compared with '/', and CRLF is read
 * as LF, since Git may write either and the CI runner is Linux while the build machine is Windows.
 */
export function sourceHash(root, sources) {
  const hash = createHash('sha256');
  const files = sources.flatMap((s) => filesUnder(root, s)).sort();
  for (const file of files) {
    const text = readFileSync(join(root, file), 'utf8').replace(/\r\n/g, '\n');
    hash.update(`${file}\n${text}\n`);
  }
  return hash.digest('hex');
}

/** The source hash a binary was built from, read by its crate's marker, or null when it carries none. */
export function stampOf(bytes, env) {
  const match = new RegExp(`${env}:([0-9a-f]{64})`).exec(bytes.toString('latin1'));
  return match ? match[1] : null;
}

/**
 * Why a built library does not carry the expected stamp, or null when it does.
 * @remarks The workflows call it through `verify` rather than grep. The same `grep -aq` passed on the
 * Linux and Windows runners (GNU grep) and failed on both macOS jobs (BSD grep), which is not a check the
 * gate would make: this is the reading the gate itself does, on any runner.
 */
export function stampMismatch(bytes, env, expected) {
  const stamp = stampOf(bytes, env);
  if (stamp === expected) return null;
  return stamp ? `built from ${stamp}, source is ${expected}` : `carries no ${env} stamp, source is ${expected}`;
}

/** Each binary present whose stamp is not its crate's current source hash. A missing one is not judged. */
export function staleBinaries(root = ROOT, crates = CRATES) {
  return crates.flatMap((c) => {
    const expected = sourceHash(root, c.sources);
    return c.binaries
      .filter((path) => existsSync(join(root, path)))
      .map((path) => ({ crate: c.name, path, stamp: stampOf(readFileSync(join(root, path)), c.env), expected }))
      .filter((b) => b.stamp !== expected);
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [command, name] = process.argv.slice(2);
  if (command === 'hash') {
    const c = CRATES.find((x) => x.name === name);
    if (!c) {
      console.error(`usage: node scripts/native-stamp.mjs hash ${CRATES.map((x) => x.name).join('|')}`);
      process.exit(2);
    }
    console.log(sourceHash(ROOT, c.sources));
  } else if (command === 'verify') {
    // verify <crate> <file>: the library a build produced carries the current source's stamp.
    const c = CRATES.find((x) => x.name === name);
    const file = process.argv[4];
    if (!c || !file) {
      console.error(`usage: node scripts/native-stamp.mjs verify ${CRATES.map((x) => x.name).join('|')} <file>`);
      process.exit(2);
    }
    const problem = stampMismatch(readFileSync(resolve(process.cwd(), file)), c.env, sourceHash(ROOT, c.sources));
    if (problem) {
      console.error(`${file} ${problem}`);
      process.exit(1);
    }
    console.log(`${file} carries ${c.env}:${sourceHash(ROOT, c.sources)}`);
  } else if (command === 'check') {
    const present = CRATES.flatMap((c) => c.binaries).filter((p) => existsSync(join(ROOT, p)));
    const stale = staleBinaries();
    for (const b of stale)
      console.error(`   stale  ${b.path} — ${b.stamp ? `built from ${b.stamp.slice(0, 12)}…` : 'carries no source stamp'}, source is ${b.expected.slice(0, 12)}…`);
    if (stale.length) {
      const names = [...new Set(stale.map((b) => b.crate))].join(' ');
      console.error(`   Fix: node scripts/refresh-native.mjs ${names}          (win-x64, from Windows)`);
      console.error(`        node scripts/refresh-native.mjs ${names} --linux  (linux-x64 as well, needs Docker)`);
      process.exit(1);
    }
    console.log(`${present.length} binaries built from the current source`);
  } else {
    console.error('usage: node scripts/native-stamp.mjs hash <crate> | check | verify <crate> <file>');
    process.exit(2);
  }
}
