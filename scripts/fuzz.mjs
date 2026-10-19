#!/usr/bin/env node
/**
 * Coverage-guided fuzzing of the .NET parsers that read untrusted input (#158): SharpFuzz instruments the
 * assemblies a harness reaches, libFuzzer drives it through libfuzzer-dotnet.
 *
 *   node scripts/fuzz.mjs --list
 *   node scripts/fuzz.mjs <target> [--seconds N]        one harness, N seconds (default 60)
 *   node scripts/fuzz.mjs --all [--seconds N]           every harness, N seconds each
 *
 * A harness is a `*.Fuzz` project under a module's `fuzz/` folder. Its `fuzz.json` names its targets and the
 * assemblies each one reaches; `FuzzTargets.All` maps the same names to code, and a test keeps the two equal.
 * Beside it:
 *   - `Corpus/<target>/`: the seeds a run starts from, committed;
 *   - `Regressions/<target>/`: every input that once crashed, committed. The module's tests replay both, so the
 *     gate runs them on every change.
 * What a run produces goes to `artifacts/fuzz/<target>/`: the corpus it grew, and `crashes/` with the input of
 * any crash. A crash is the exit code: the run fails.
 *
 * libFuzzer needs Linux. Anywhere else the script runs itself in the .NET SDK image, on a copy of the
 * repository (a Linux build in the working tree would break the Windows one), and copies the results back.
 */
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/**
 * The libFuzzer driver, pinned to a commit of github.com/Metalnem/libfuzzer-dotnet (tag v2025.05.02.0904) and
 * to the hash of its source: it is compiled here, so a changed file is refused rather than built.
 */
export const DRIVER = {
  url: 'https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/bd39d4e88d715ab460a929943645be2a186cde52/libfuzzer-dotnet.cc',
  sha256: '90f019e2e9ad3a0b93c7ecc2c5afb2fbfc8b5aab6aac51c7e0d349ec79354f36',
};

/** The instrumenter, the same version as the SharpFuzz package in Directory.Packages.props. */
export const SHARPFUZZ_VERSION = '2.3.0';

/** The image a non-Linux machine runs in: the SDK of global.json's major version, on Ubuntu. */
const IMAGE = 'mcr.microsoft.com/dotnet/sdk:10.0';

/**
 * Every harness project and its targets, read from the `fuzz.json` files under the modules' `fuzz/` folders.
 *
 * @returns {{ name: string, project: string, instrument: string[] }[]} `project` relative to the root
 */
export function findTargets(root = ROOT) {
  const targets = [];
  for (const module of readdirSync(root, { withFileTypes: true })) {
    const fuzz = join(root, module.name, 'fuzz');
    if (!module.isDirectory() || !existsSync(fuzz)) continue;
    for (const project of readdirSync(fuzz, { withFileTypes: true })) {
      const manifest = join(fuzz, project.name, 'fuzz.json');
      if (!project.isDirectory() || !existsSync(manifest)) continue;
      const { targets: declared } = JSON.parse(readFileSync(manifest, 'utf8'));
      for (const [name, { instrument }] of Object.entries(declared))
        targets.push({ name, project: relative(root, join(fuzz, project.name)).replaceAll('\\', '/'), instrument });
    }
  }
  return targets.sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * libFuzzer's arguments for one run: the driver starts `dotnet <assembly>`, and the harness reads its target
 * from the environment. The grown corpus comes first, so new inputs are written there and not among the seeds.
 */
export function libFuzzerArgs({ assembly, seconds, corpus, seeds, crashes }) {
  return [
    '--target_path=dotnet',
    `--target_arg=${assembly}`,
    '-timeout=10',
    `-max_total_time=${seconds}`,
    '-rss_limit_mb=2560',
    `-artifact_prefix=${crashes}/`,
    corpus,
    seeds,
  ];
}

function run(command, args, options = {}) {
  console.log(`> ${command} ${args.join(' ')}`);
  const r = spawnSync(command, args, { stdio: 'inherit', ...options });
  if (r.error) throw r.error;
  return r.status ?? 1;
}

function must(command, args, options) {
  const status = run(command, args, options);
  if (status !== 0) {
    console.error(`FAIL: ${command} exited ${status}`);
    process.exit(status);
  }
}

async function driver(work) {
  const binary = join(work, 'driver', 'libfuzzer-dotnet');
  if (existsSync(binary)) return binary;
  mkdirSync(dirname(binary), { recursive: true });

  const response = await fetch(DRIVER.url);
  if (!response.ok) throw new Error(`could not download the driver: ${response.status}`);
  const source = Buffer.from(await response.arrayBuffer());
  const hash = createHash('sha256').update(source).digest('hex');
  if (hash !== DRIVER.sha256) {
    console.error(`FAIL: libfuzzer-dotnet.cc hashes to ${hash}, expected ${DRIVER.sha256}`);
    process.exit(1);
  }

  const file = join(work, 'driver', 'libfuzzer-dotnet.cc');
  writeFileSync(file, source);
  must('clang', ['-Werror', '-O2', '-fsanitize=fuzzer', file, '-o', binary]);
  return binary;
}

function sharpfuzz(work) {
  const tools = join(work, 'tools');
  const tool = join(tools, 'sharpfuzz');
  if (!existsSync(tool))
    must('dotnet', ['tool', 'install', 'SharpFuzz.CommandLine', '--version', SHARPFUZZ_VERSION, '--tool-path', tools]);
  return tool;
}

async function fuzzOne(target, seconds, work) {
  const project = join(ROOT, target.project);
  const bin = join(work, target.name, 'bin');
  // The version pinned, as the gate does: the copy a non-Linux machine fuzzes has no .git for MinVer to read.
  must('dotnet', ['publish', project, '-c', 'Release', '-o', bin, '-p:MinVerVersionOverride=0.0.0-fuzz']);

  const instrument = sharpfuzz(work);
  for (const dll of target.instrument) must(instrument, [join(bin, dll)]);

  const corpus = join(work, target.name, 'corpus');
  const crashes = join(work, target.name, 'crashes');
  mkdirSync(corpus, { recursive: true });
  mkdirSync(crashes, { recursive: true });
  // A crash from an earlier local run stays where it was written until someone commits it; this run reports only
  // the inputs it wrote, or every later run would fail on a crash already fixed.
  const earlier = new Set(readdirSync(crashes));

  const assembly = join(bin, `${basename(target.project)}.dll`);
  const seeds = join(project, 'Corpus', target.name);
  const status = run(await driver(work), libFuzzerArgs({ assembly, seconds, corpus, seeds, crashes }), {
    env: { ...process.env, PRAGMATIC_FUZZ_TARGET: target.name },
  });

  const found = readdirSync(crashes).filter(name => !earlier.has(name));
  if (status !== 0 || found.length > 0) {
    console.error(`FAIL: ${target.name} crashed; inputs in ${crashes}: ${found.join(', ') || '(none written)'}`);
    console.error(`Commit each one under ${target.project}/Regressions/${target.name}/ with the fix.`);
    return false;
  }

  console.log(`ok: ${target.name}, ${seconds}s, corpus of ${readdirSync(corpus).length} inputs`);
  return true;
}

// libFuzzer needs Linux: anywhere else, the same command in the SDK image, on a copy of the repository.
function inDocker(args) {
  const out = join(ROOT, 'artifacts', 'fuzz');
  mkdirSync(out, { recursive: true });
  const script = [
    'set -e',
    'apt-get update -qq && apt-get install -y -qq clang nodejs > /dev/null',
    'mkdir /work && tar -C /src --exclude=./.git --exclude=./artifacts --exclude=bin --exclude=obj --exclude=node_modules -cf - . | tar -C /work -xf -',
    'mkdir -p /work/artifacts && ln -s /out /work/artifacts/fuzz',
    `cd /work && node scripts/fuzz.mjs ${args.map((a) => `'${a}'`).join(' ')}`,
  ].join('\n');
  writeFileSync(join(out, 'docker-run.sh'), script);
  return run('docker', ['run', '--rm', '-v', `${ROOT}:/src:ro`, '-v', `${out}:/out`, IMAGE, 'bash', '/out/docker-run.sh']);
}

async function main(args) {
  const all = findTargets();
  if (args.includes('--list')) {
    for (const t of all) console.log(`${t.name}  (${t.project})`);
    return 0;
  }

  const at = args.indexOf('--seconds');
  const seconds = at >= 0 ? Number(args[at + 1]) : 60;
  const names = args.includes('--all') ? all.map((t) => t.name) : args.filter((a, i) => !a.startsWith('--') && args[i - 1] !== '--seconds');
  if (names.length === 0 || !Number.isInteger(seconds) || seconds <= 0) {
    console.error('usage: node scripts/fuzz.mjs <target>|--all [--seconds N] | --list');
    return 2;
  }

  const unknown = names.filter((n) => !all.some((t) => t.name === n));
  if (unknown.length > 0) {
    console.error(`unknown target(s): ${unknown.join(', ')}. Known: ${all.map((t) => t.name).join(', ')}`);
    return 2;
  }

  if (process.platform !== 'linux') return inDocker(args);

  const work = join(ROOT, 'artifacts', 'fuzz');
  let green = true;
  for (const name of names) green = (await fuzzOne(all.find((t) => t.name === name), seconds, work)) && green;
  return green ? 0 : 1;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exit(await main(process.argv.slice(2)));
}
