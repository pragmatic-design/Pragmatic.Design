#!/usr/bin/env node
/**
 * check — THE gate for this repo. One definition, used both locally and by CI.
 *
 * Why this exists
 * ---------------
 * Five ways of getting a verdict that meant nothing. A gate that goes green at random and one that
 * goes red at random are equally unusable, and this file has now been bitten by both:
 *
 *   1. `dotnet build --warnaserror` WITHOUT --no-incremental reports 0 errors while the same sources
 *      clean-build with 14. MSBuild does not re-evaluate projects it considers up to date, so the flag
 *      only checks whatever happened to be rebuilt. Every gate tier here builds clean.
 *   2. `dotnet test` over the whole solution starts the container-based suites in parallel. Twenty of
 *      them run Testcontainers; together they exhaust Docker, tests fail on container start-up, and the
 *      failures look like code regressions. They are marked <RequiresDocker>true</RequiresDocker> and
 *      run ONE AT A TIME here.
 *   3. CI referenced solution files that do not exist, in jobs gated on `pull_request` — which a
 *      trunk-based flow never produces. The jobs were dead and nobody noticed, so `--verify` checks
 *      that every path CI names is real.
 *   4. MinVer shells out to git once per project. Across 150+ projects one of those invocations
 *      sporadically fails ("MINVER1007: git is not present in PATH") and takes the build down for a
 *      reason unrelated to the code — so the gate pins the version instead.
 *   5. Serialising the container suites fixed the saturation but not the accumulation: the daemon still
 *      degraded after ~7 suites, and the last four reported failures while every hermetic test in them
 *      passed. The tier now prunes between suites and, if a suite fails, checks whether Docker is still
 *      answering before blaming the code.
 *   6. The gate shared bin/ and obj/ with whatever else was running. It deletes nothing, but rewriting
 *      an output means removing the previous one, and a file held open fails the build with MSB3061 —
 *      naming a project nobody touched. So the gate builds into artifacts/build,
 *      so there is nothing to contend for with an IDE and the machine stays usable while it runs.
 *      That alone was NOT enough: a test host outliving its suite holds files INSIDE the isolated
 *      directory and fails the next clean build just the same. Isolation handles other processes;
 *      the orphan sweep handles this one's own. Both are needed.
 *   7. "Nothing failed" is not "everything ran". A test host that dies partway through reports the
 *      tests it finished, none of them failed, and a gate that only asks "did anything fail" goes green
 *      having executed fewer than exist. Counts per suite are recorded, a partial drop is called out,
 *      and a drop to ZERO FAILS: a host that overflows its stack reports neither a pass nor a failure,
 *      so calling out alone would print `Showcase.IntegrationTests: 511 -> 0` followed by GREEN for
 *      the one case where the suite did not run at all.
 *
 * Usage
 * -----
 *   node scripts/check.mjs --tier fast     # build + hermetic tests for changed modules (dev loop)
 *   node scripts/check.mjs --tier full     # clean build --warnaserror + ALL hermetic tests
 *   node scripts/check.mjs --tier aot      # publish the AOT smokes and run them
 *   node scripts/check.mjs --tier docker   # the container suites, sequentially
 *   node scripts/check.mjs --tier all      # full, then aot, then docker  ← the real gate
 *   node scripts/check.mjs --tier verify   # only check that CI-referenced paths exist
 *
 *   --jobs N     hermetic parallelism (default: cores-2, min 2)
 *   --docker-jobs N  container suites at once (default: 1 — see dockerJobs)
 *   --keep-going don't stop at the first failing suite
 *
 * Exit code is 0 only if everything requested passed.
 */
import { execSync, spawnSync } from 'node:child_process';
import { runAsync, runPool } from './lib/spawn-async.mjs';
import { acquire as acquireRunLock, describeHolder } from './lib/run-lock.mjs';
import { record as recordFailureRun, failureDetails, failureLines } from './lib/failure-record.mjs';
import { TESTCONTAINERS_LABEL, ownTestHosts } from './lib/gate-ownership.mjs';
import { measureCoverage } from './lib/example-coverage.mjs';
import {
  COVERAGE_REASONS, PACKAGE_REASONS, reasonsWithMissingProofs,
} from './lib/example-coverage-reasons.mjs';
import { measureAttributeReaders } from './lib/attribute-readers.mjs';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, basename, dirname } from 'node:path';
import { cpus } from 'node:os';
import { supplyChain } from './supply-chain.mjs';
import { nativeNameMismatch, packageId, packagesWrittenSince, packedSince } from './lib/packed-packages.mjs';
import { zipEntries } from './package-markdown-links.mjs';
import { skillCoverage, BUDGET as SKILL_BUDGET } from './skill-coverage.mjs';
import { silentDrops, BUDGET as SILENT_BUDGET } from './silent-drops.mjs';
import { hostProvidedContracts, BUDGET as CONTRACTS_BUDGET } from './host-provided-contracts.mjs';

// The gate owns its processes and leaves none behind.
//
// ⚠️ MSBuild keeps its worker nodes alive after a build so the next one can reuse them, and the next
// one here is minutes or days away. One `--tier full` from a machine at zero leaves dozens of
// `dotnet.exe`, all of them MSBuild nodes, holding gigabytes. They accumulate across runs, and what
// they produce is not an error — it is a slower run that quietly does less: a full tier several times
// slower, and **suites that execute no tests at all**, reporting thousands fewer passed. Only the ratchet on a suite that ran no tests after running some stands
// between that and a green.
//
// An environment variable rather than `-nodeReuse:false` on one invocation: every child inherits it,
// so the build, the test hosts and anything spawned later are covered by one line instead of by
// remembering to repeat a flag.
process.env.MSBUILDDISABLENODEREUSE = '1';

const SLN = 'Pragmatic.Design.slnx';
const args = process.argv.slice(2);
const flag = (name, fallback = null) => {
  const i = args.indexOf(name);
  return i >= 0 ? (args[i + 1] ?? true) : fallback;
};
const tier = flag('--tier', 'fast');
const keepGoing = args.includes('--keep-going');
/**
 *     Run only the suites whose project name contains this, within whatever tier was asked for.
 *
 * The whole gate takes a quarter of an hour or more, and a story pays it for every change, which is what made everyone reach for
 * a bare `dotnet test` instead - and a bare `dotnet test` is the thing this script exists to replace,
 * because it builds incrementally and reports green on sources that do not compile clean.
 *
 * So: one canonical command for "run only what my change can reach", rather than a habit of leaving
 * the gate. It narrows the SUITES; every ratchet and the clean build still run, because those are the
 * half that catches what a suite cannot see.
 */
const only = flag('--only', null);

/**
 * How many hermetic suites run at once. **Eight** by default, not one per core.
 *
 * ⚠️ Not `cpus().length - 2` — thirty on a large machine — because each of those thirty is a test
 * process that parallelises internally, so the machine is oversubscribed several times over. Tests that
 * measure time then fail on scheduling rather than on a defect — a job that has not started within its
 * deadline, a cancellation that has not been observed yet.
 *
 * The width buys almost nothing. The tier's duration is the makespan of its slowest suites, not the
 * sum of all of them, so a wider pool saves seconds. Seconds are not worth a gate that goes red at
 * random, and a gate that goes red at random stops meaning the code is broken — the same reasoning
 * that already keeps the container tier at one.
 *
 * `--jobs N` still opens it up for anyone who wants to measure again.
 */
const jobs = Math.max(2, Number(flag('--jobs', Math.min(8, Math.max(2, cpus().length - 2)))));

/**
 * How many container suites run at once. **One** by default, and that default is a decision rather
 * than an accident: running them together once saturated Docker and produced 26 red tests that were
 * all green in isolation, which is the failure a gate cannot have.
 *
 * ⚠️ "It has always been fine" is not evidence for moving it: a runner that never overlaps anything
 * would make the tier sequential whatever this says. The knob exists so the question can be
 * measured against a runner that does overlap; the bar for moving the default is five consecutive
 * green runs of `--tier docker`, because the flake it guards against only appears under contention.
 */
const dockerJobs = Math.max(1, Number(flag('--docker-jobs', 1)));

/**
 * Rejects anything not recognised, instead of falling through to the default tier.
 *
 * Ignoring an unknown argument would leave `tier` at 'fast' and start a build: asking for `--help`,
 * or for `--verify` — the flag is `--tier verify` — would compile the solution. A gate that runs when you asked it not to is a gate you
 * stop trusting with anything.
 */
const known = new Set(['--tier', '--jobs', '--docker-jobs', '--keep-going', '--only']);
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith('--')) continue; // a value belonging to the flag before it
  if (known.has(arg)) {
    if (arg !== '--keep-going') i++; // skip its value
    continue;
  }
  console.error(`unknown option '${arg}'\n`);
  console.error('usage: node scripts/check.mjs [--tier fast|full|aot|docker|all|verify] [--jobs N] [--keep-going] [--only <substring>]');
  process.exit(2);
}

const ok = (s) => `\x1b[32m${s}\x1b[0m`;
const bad = (s) => `\x1b[31m${s}\x1b[0m`;
const dim = (s) => `\x1b[2m${s}\x1b[0m`;

// ── discovery ────────────────────────────────────────────────────────────────

/**
 * Every test project, skipping build output, node_modules and the untracked `.internals/`, whose
 * templates are not part of the repository.
 *
 * Matches "…Tests.csproj" without requiring a dot before Tests: Showcase.IntegrationTests.csproj does
 * not end in ".Tests.csproj", and requiring the dot would silently leave the E2E suite — the one that
 * exercises the generated code against a real database — out of the gate entirely.
 */
function findTestProjects(dir = '.', acc = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin' || entry.name === 'node_modules' || entry.name === '.git'
      || entry.name === '.internals')
      continue;
    const p = join(dir, entry.name);
    if (entry.isDirectory()) findTestProjects(p, acc);
    else if (/Tests\.csproj$/.test(entry.name)) acc.push(p);
  }
  return acc;
}

/**
 * Docker-dependent suites declare it in their own csproj. A list kept in this script would drift the
 * first time someone adds a Testcontainers fixture; a property next to the project does not.
 */
const requiresDocker = (proj) => readFileSync(proj, 'utf8').includes('<RequiresDocker>true</RequiresDocker>');

function changedModules() {
  try {
    const out = execSync('git diff --name-only HEAD', { encoding: 'utf8' });
    const mods = new Set();
    for (const line of out.split('\n')) {
      const m = line.match(/^([^/]+)\//);
      if (m) mods.add(m[1]);
    }
    return mods;
  } catch {
    return new Set();
  }
}

// ── running ──────────────────────────────────────────────────────────────────

/**
 * Runs a command and captures its output.
 *
 * maxBuffer is raised well past the 1 MB default: a clean build of this solution emits ~1.05 MB of
 * warnings, spawnSync then fails with ENOBUFS, `status` comes back null, and treating that as a
 * non-zero exit reported a perfectly good build as FAIL with "0 error markers". `spawnError` is kept
 * separate from the exit code so an infrastructure failure can never be read as a compile failure.
 */
function run(cmd, cmdArgs) {
  const r = spawnSync(cmd, cmdArgs, {
    encoding: 'utf8',
    shell: process.platform === 'win32',
    maxBuffer: 512 * 1024 * 1024,
  });
  return {
    code: r.status ?? (r.error ? -1 : 1),
    out: `${r.stdout ?? ''}${r.stderr ?? ''}`,
    spawnError: r.error ? String(r.error) : null,
  };
}

/**
 * Reads the run's counts, in either of the two shapes `dotnet test` prints.
 *
 * ⚠️ There are two, and they are not variations of one. The default console logger ends with a
 * single line — `Superato! - Non superati: 0. Superati: 2. …` — while a **detailed** logger ends with
 * a block, one count per line: `Totale test: 8` / `Superati: 8`. A parser that reads only the first,
 * with a loose alternation, matches something else in the block: a suite of 8 passing tests is
 * reported as `8 failed, 8 passed`. The gate runs at detailed verbosity to capture failure messages,
 * so the block is the shape it usually sees.
 *
 * The block is tried first, anchored to the start of a line, which is what separates
 * `\n     Non superati: 2` from the `- Non superati: 0.` sitting mid-line in the other shape.
 * Field-position parsing is still a trap: "Non superati:  22" splits oddly.
 */
function parseTestOutput(out) {
  const num = (re) => {
    const m = out.match(re);
    return m ? Number(m[1]) : 0;
  };
  const block = (labels) => num(new RegExp(`^\\s*(?:${labels}):\\s+(\\d+)`, 'm'));
  const line = (labels) => num(new RegExp(`(?:${labels}):\\s+(\\d+)`));

  // A detailed logger prints "Totale test:" / "Total tests:"; its absence means the one-line shape.
  const detailed = /^\s*(?:Totale test|Total tests):\s+\d+/m.test(out);
  const count = detailed ? block : line;

  return {
    passed: count('Superati|Passed'),
    failed: count('Non superati|Failed'),
    skipped: count('Ignorati|Skipped'),
    noTests: /Nessun test|No test is available/i.test(out),
    buildError: /error (CS|MSB|NETSDK)\d+/.test(out),
    failures: parseFailedTests(out),
  };
}

/**
 * Names of the individual tests that failed, each with its reason capped to a terminal line.
 *
 * The runner prints them, and the gate passes them on rather than reporting only "suite X is red":
 * otherwise every red gate starts with a re-run to find out what actually broke, and a failure that
 * does not reproduce (a suite that passes alone and fails under the parallel load of the full run)
 * cannot be diagnosed at all.
 *
 * ⚠️ This is the TERMINAL's share and it stays capped. What the record keeps is
 * {@link failureDetails}, beside it and not instead of it. Both live in
 * `scripts/lib/failure-record.mjs`, driven by a real run's captured output, because the scar this
 * function carries is a shape that was checked against invented strings and silently matched nothing.
 */
function parseFailedTests(out) {
  return failureLines(out);
}


/**
 * Every shipped project must be IN the solution, or the gate never compiles it and the pack never
 * produces it.
 *
 * A project outside `Pragmatic.Design.slnx` can compile fine on its own, be named in a sibling's
 * `InternalsVisibleTo` and advertised in a package description — and still never be built by the gate
 * or packed into a `.nupkg`, so nobody can install it. Nothing says so: a project that is absent from
 * the solution is absent from every signal the solution produces. This check is that signal.
 */
/**
 * A skill's examples/ folder: copies of tested files, kept for reading (sync-skill-examples.mjs). A
 * project or a test file there is documentation of one that is built and run where it came from.
 */
const SKILL_EXAMPLE = /^marketplace\/plugins\/[^/]+\/skills\/[^/]+\/examples\//;

function everyProjectIsInTheSolution() {
  process.stdout.write('  every src project is in the solution … ');
  const slnx = readFileSync(SLN, 'utf-8');
  const tracked = execSync('git ls-files "*/src/*.csproj" "shared/**/*.csproj"', { encoding: 'utf-8' })
    .trim().split('\n').filter(Boolean)
    // The module template is a scaffold with placeholder names; it is not a project that ships.
    .filter((p) => !p.includes('templates/'))
    .filter((p) => !SKILL_EXAMPLE.test(p));

  const missing = tracked.filter((p) => !slnx.includes(p) && !slnx.includes(p.replace(/\//g, '\\')));
  if (missing.length > 0) {
    console.log(bad(`FAIL (${missing.length} outside the solution)`));
    for (const m of missing) console.log(`    ${m}`);
    console.log('    Add them to Pragmatic.Design.slnx: outside it they are never built and never packed.');
    return false;
  }
  console.log(`${ok('ok')} ${dim(`(${tracked.length} projects)`)}`);
  return true;
}

/**
 * Every tracked test source must belong to a project, or nothing ever compiles it.
 *
 * `Pragmatic.Messaging/tests/Pragmatic.Messaging.Core.Tests/` held one file with two tests
 * and **no `.csproj`**. No sibling project reached it either — default globs do not cross project
 * directories — so those two tests had never run. A repository-wide pass edited the file (the
 * FluentAssertions removal, `a971b8435`) and got no signal back, because there was nothing to give it.
 *
 * ⚠️ `findTestProjects` walks for `*Tests.csproj`, so the gate's own "not in the solution" warning
 * cannot fire for this: a folder with no project file is not a suite that is missing from the
 * solution, it is not a suite at all. This check looks at the other end — the files — and asks
 * whether any project above them could compile them.
 *
 * What it sees: a `.cs` file under a `tests/` folder with no `.csproj` in itself or any ancestor up to
 * that folder. What it does not see: a file a project excludes with `<Compile Remove>`, which is a
 * deliberate act that leaves a trace in the csproj.
 */
function everyTestSourceHasAProject() {
  process.stdout.write('  every test source belongs to a project … ');

  const tracked = execSync('git ls-files "*/tests/*.cs" "tests/*.cs"', { encoding: 'utf-8' })
    .trim().split('\n').filter(Boolean)
    // The module template is a scaffold with placeholder names; it is not a suite that runs.
    .filter((p) => !p.includes('templates/'))
    .filter((p) => !SKILL_EXAMPLE.test(p));

  const orphaned = tracked.filter((file) => {
    // Up from the file, stopping at the `tests` folder itself: a project outside it does not glob in.
    let dir = dirname(file);
    while (dir && dir !== '.' && basename(dir) !== 'tests') {
      if (readdirSync(dir).some((entry) => entry.endsWith('.csproj')))
        return false;
      dir = dirname(dir);
    }
    return true;
  });

  if (orphaned.length > 0) {
    console.log(bad(`FAIL (${orphaned.length} compiled by nothing)`));
    for (const o of orphaned) console.log(`    ${o}`);
    console.log('    A test file no project compiles has never run and never will, and no suite count');
    console.log('    reports it. Give the folder a .csproj and put it in the solution, or delete it.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(`(${tracked.length} files)`)}`);
  return true;
}

/**
 * A ratchet on how much of the public surface no skill mentions.
 *
 * 75 of 215 attributes were invisible when this was first measured, 47 after the first pass, and one of them —
 * [Autocomplete] — generates a whole search endpoint from a single property and ships in the
 * Showcase. Nobody could have found it: a consumer reads the skills, and an agent does not search
 * for what it has no reason to suspect. The number is allowed to fall and never to rise, so the
 * next feature cannot be born invisible the same way.
 */
function skillCoverageRatchet() {
  process.stdout.write('  skills name the public surface … ');
  const { total, missing } = skillCoverage();

  if (missing.length > SKILL_BUDGET) {
    console.log(bad(`FAIL (${missing.length} of ${total} unmentioned, budget ${SKILL_BUDGET})`));
    for (const m of missing.slice(0, 40)) console.log(`    [${m.name}]  ${m.module}`);
    console.log('    A new attribute nothing documents is a feature nobody can find. Document it,');
    console.log('    or lower the budget only after the count has genuinely come down.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(`(${missing.length} of ${total} unmentioned, budget ${SKILL_BUDGET})`)}`);
  return true;
}

/**
 * A ratchet on how many attribute-triggered transforms cannot report a rejection at all.
 *
 * A transform reached through ForAttributeWithMetadataName is looking at a node the user decorated, so
 * `return null` means either "not the shape I handle" or "yours, and malformed" — and the caller cannot
 * tell them apart. The second ships as silence: the attribute compiles, nothing is generated, the build
 * is green. Nine of those reached two consumer applications in one day, found by accident. The number
 * is allowed to fall and never to rise, so the tenth cannot be born the same way.
 *
 * ⚠️ A transform that answers with a value-equatable **carrier** — `…DiagnosticInfo` — has a channel
 * and is not counted, even though it never constructs a `Diagnostic`: it cannot, because a
 * `Diagnostic` in an incremental pipeline breaks caching. Reading that pattern as silence would count
 * the one file whose whole job is to report, and fail the next transform anywhere in the generator for
 * a reason that is not its own. The rule has its own cases: `scripts/silent-drops.test.mjs`.
 */
function silentDropsRatchet() {
  process.stdout.write('  transforms that can report a rejection … ');
  const { total, silent, drops } = silentDrops();

  if (drops > SILENT_BUDGET) {
    console.log(bad(`FAIL (${drops} drop sites, budget ${SILENT_BUDGET})`));
    for (const s of silent.slice(0, 20)) console.log(`    ${s.drops}  ${s.file}`);
    console.log('    A transform that drops a decorated node without a diagnostic is a feature that');
    console.log('    compiles and does nothing. Give it a way to say why, or lower the budget only');
    console.log('    after the count has genuinely come down.');
    console.log('    node scripts/silent-drops.mjs --lines  prints file:line for every one of them.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(`(${drops} drop sites in ${silent.length} of ${total} transforms, budget ${SILENT_BUDGET})`)}`);
  return true;
}

/**
 * A ratchet on the contracts a package registers and does not declare.
 *
 * `[ProvidedByHost]` is how a contract the host registers says so, and it keeps the copy of the name
 * out of the generator. On its own it does not make anything fail when a package forgets
 * it: the build stays green here and the failure lands on the first application that injects the
 * contract from a `[Service]`, as PRAG1641 on code that is correct. This ratchet makes the omission
 * fail here instead.
 */
function hostProvidedContractsRatchet() {
  process.stdout.write('  a contract a package registers says who registers it … ');
  const { registrations, declared, undeclared } = hostProvidedContracts();

  if (undeclared.length > CONTRACTS_BUDGET) {
    console.log(bad(`FAIL (${undeclared.length} undeclared, budget ${CONTRACTS_BUDGET})`));
    for (const entry of undeclared.slice(0, 20)) console.log(`    ${entry.contract}  ${entry.file}`);
    console.log('    A contract a module could inject wants [ProvidedByHost] in the package that');
    console.log('    registers it, or the application that injects it is refused PRAG1641 on correct');
    console.log('    code. Declare it, or lower the budget only after the count has come down.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(
    `(${undeclared.length} undeclared of ${declared + undeclared.length} registered, `
    + `${registrations} registrations, budget ${CONTRACTS_BUDGET})`)}`);
  return true;
}

/**
 *     How long something took, in the shortest form that stays readable: "8.4s", "3m 12s".
 *
 * Every line says how long it took, not only what passed: without that, every proposal to make the
 * gate faster is a guess. Wall clock, not CPU: what is being optimised is the wait.
 */
function took(startedAt) {
  const ms = performance.now() - startedAt;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const total = Math.round(ms / 1000);
  return `${Math.floor(total / 60)}m ${String(total % 60).padStart(2, '0')}s`;
}

function buildSolution({ clean }) {
  const label = clean ? 'clean build --warnaserror' : 'build --warnaserror';
  process.stdout.write(`  ${label} … `);
  const a = [
    'build', SLN, '-warnaserror', '-v', 'q', '--nologo',
    // Pin the version so MinVer does not shell out to git once per project. Building the solution
    // fires it 150+ times in parallel, and one of those invocations sporadically fails with
    // "MINVER1007: git is not present in PATH" — which fails the whole build for a reason that has
    // nothing to do with the code. A gate that goes red at random is no more usable than one that
    // goes green at random. The value is irrelevant here: the gate compiles and tests, it never packs,
    // and snapshot tests already scrub the version.
    '-p:MinVerVersionOverride=0.0.0-gate',
    ...ARTIFACTS,
  ];
  if (clean) a.push('--no-incremental');
  const startedAt = performance.now();
  lastBuildStartedAt = Date.now();
  const { code, out, spawnError } = run('dotnet', a);
  if (spawnError) {
    console.log(bad(`COULD NOT RUN — ${spawnError}`));
    return false;
  }
  const errors = [...out.matchAll(/error [A-Z]+\d+/g)].length;
  if (code !== 0 || errors > 0) {
    console.log(bad(`FAIL (${errors} error markers, exit ${code})`));
    console.log(out.split('\n').filter((l) => /error /.test(l)).slice(0, 25).join('\n'));
    return false;
  }
  // Read MSBuild's own total rather than counting "warning XXNNNN" markers: each one appears twice in
  // the log (once at the project, once in the summary), so counting them reported double — 3076 for a
  // build MSBuild called 1538. A gate that inflates its own numbers is the problem it exists to fix.
  const summary = [...out.matchAll(/^\s*(?:Avvisi|Warning\(s\)):\s+(\d+)/gm)].pop();
  const warnings = summary
    ? Number(summary[1])
    : [...new Set(out.split('\n').filter((l) => /warning [A-Z]+\d+/.test(l)))].length;
  console.log(`${ok('ok')} ${dim(`(${warnings} warnings, ${took(startedAt)})`)}`);
  lastBuildOutput = out;
  return true;
}

/**
 * The last clean build's output, so the trim/AOT ratchet can read it without building twice.
 */
let lastBuildOutput = '';

/** When the last build started (ms since the epoch): the packages it wrote are the ones newer than this. */
let lastBuildStartedAt = 0;

/**
 * Only Pragmatic.* comes out of the build as a package. Anything else the build files classify as a
 * runtime library packs on build and reaches the feed on the next publish-local. The build packs into
 * artifacts/build/package; only what this build wrote
 * is judged, since the folder keeps packages from older builds, and a build that packed nothing fails
 * rather than passes, because then this measures nothing.
 */
function onlyPragmaticPackagesArePacked() {
  process.stdout.write('  the build packs only Pragmatic packages, natives loadable by name … ');
  const test = 'scripts/packed-packages.test.mjs';
  if (existsSync(test)) {
    const self = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });
    if (self.status !== 0) {
      console.log(bad('FAIL'));
      for (const line of `${self.stdout || ''}${self.stderr || ''}`.split('\n'))
        if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
      return false;
    }
  }

  const ids = packedSince('artifacts/build/package', lastBuildStartedAt);
  if (ids.length === 0) {
    console.log(bad('FAIL — the build packed nothing, so there is nothing to judge'));
    return false;
  }
  const foreign = ids.filter((id) => !id.startsWith('Pragmatic.')).sort();
  if (foreign.length > 0) {
    console.log(bad(`FAIL — ${foreign.length} package(s) that are not Pragmatic.*: ${foreign.join(', ')}`));
    console.log('    A tool, sample or example is not a library: exclude it in Directory.Build.props or set IsPackable=false.');
    return false;
  }

  // Every platform's native asset under the one name the P/Invoke asks for.
  const misnamed = packagesWrittenSince('artifacts/build/package', lastBuildStartedAt)
    .map((file) => [packageId(basename(file)), nativeNameMismatch([...zipEntries(readFileSync(file)).keys()])])
    .filter(([, mismatch]) => mismatch !== null);
  if (misnamed.length > 0) {
    console.log(bad(`FAIL — native assets a platform cannot load by the P/Invoke name`));
    for (const [id, mismatch] of misnamed) console.log(`    ${id}: ${mismatch}`);
    console.log('    Pack each native file as X.dll / libX.so / libX.dylib, X being the [LibraryImport] name.');
    return false;
  }
  console.log(`${ok('ok')} ${dim(`(${ids.length} packages)`)}`);
  return true;
}

/**
 * A ratchet on reflective requirements that are not declared to the caller.
 *
 * See UNDECLARED_REFLECTION_BUDGET for why this is a separate count from the one below: these warnings
 * do not say "this needs dynamic code", they say "the requirement stops here and the caller is never
 * told" — the failure that arrives as a NullReferenceException in a trimmed build, months later.
 *
 * The fix is almost never a suppression: it is [DynamicallyAccessedMembers] on the type parameter, so
 * the requirement travels, or [RequiresUnreferencedCode] when the reflection genuinely cannot be
 * described.
 */
function undeclaredReflectionRatchet() {
  process.stdout.write('  undeclared reflective requirements … ');

  const lines = lastBuildOutput.split(String.fromCharCode(10))
    .filter((l) => /warning IL(2060|2070|2072|2075|2087|2090|2091)/.test(l));
  const perProject = new Map();

  for (const line of lines) {
    const match = line.match(/([A-Za-z0-9_.]+)\.csproj/);
    if (!match) continue;

    const project = match[1];
    // Same scope as the trim ratchet: only what ships as Pragmatic.
    if (!project.startsWith('Pragmatic.')) continue;

    perProject.set(project, (perProject.get(project) ?? 0) + 1);
  }

  // MSBuild prints each warning twice — at the project and again in the summary.
  const total = Math.round([...perProject.values()].reduce((a, b) => a + b, 0) / 2);

  if (total > UNDECLARED_REFLECTION_BUDGET) {
    console.log(bad(`FAIL (${total}, budget ${UNDECLARED_REFLECTION_BUDGET})`));
    for (const [project, count] of [...perProject].sort((a, b) => b[1] - a[1]).slice(0, 10))
      console.log(`    ${project}: ${Math.round(count / 2)}`);
    console.log('    Annotate the type parameter with [DynamicallyAccessedMembers] so the requirement');
    console.log('    reaches the caller, instead of stopping here and failing in a trimmed build.');
    return false;
  }

  const slack = UNDECLARED_REFLECTION_BUDGET - total;
  console.log(`${ok('ok')} ${dim(`(${total}, budget ${UNDECLARED_REFLECTION_BUDGET}${slack > 0 ? ` — lower it by ${slack}` : ''})`)}`);
  return true;
}

/**
 * A ratchet on trim/AOT warnings coming from Pragmatic's own assemblies.
 *
 * The count is allowed to fall and never to rise. It is not zero and will not be: EF Core is out of
 * scope by decision, assembly scanning is incompatible with trimming by construction (and says so
 * with RequiresUnreferencedCode), and a logging provider that renders arbitrary objects is reflective
 * by definition. What the ratchet prevents is the other thing — a new untyped serializer call, or a
 * new endpoint mapped with a handler Delegate, arriving unnoticed.
 *
 * The budget is TRIM_WARNING_BUDGET. Sample apps and the Showcase are excluded — they are allowed to demonstrate EF, the stated exception.
 */
function trimWarningRatchet() {
  process.stdout.write('  trim/AOT warnings from Pragmatic assemblies … ');

  const lines = lastBuildOutput.split(String.fromCharCode(10))
    .filter((l) => /warning IL(2026|3050)/.test(l));
  const perProject = new Map();

  for (const line of lines) {
    const match = line.match(/([A-Za-z0-9_.]+)\.csproj/);
    if (!match) continue;

    const project = match[1];
    // Only what ships as Pragmatic. A sample is allowed to show EF Core, which is the stated exception.
    if (!project.startsWith('Pragmatic.')) continue;

    perProject.set(project, (perProject.get(project) ?? 0) + 1);
  }

  // MSBuild prints each warning twice — at the project and again in the summary.
  const total = Math.round([...perProject.values()].reduce((a, b) => a + b, 0) / 2);

  if (total > TRIM_WARNING_BUDGET) {
    console.log(bad(`FAIL (${total}, budget ${TRIM_WARNING_BUDGET})`));
    for (const [project, count] of [...perProject].sort((a, b) => b[1] - a[1]).slice(0, 10))
      console.log(`    ${project}: ${Math.round(count / 2)}`);
    console.log('    Pass a JsonTypeInfo, map with a RequestDelegate, or — when the code is genuinely');
    console.log('    reflective — annotate it so the requirement is declared instead of leaked.');
    return false;
  }

  const slack = TRIM_WARNING_BUDGET - total;
  console.log(`${ok('ok')} ${dim(`(${total}, budget ${TRIM_WARNING_BUDGET}${slack > 0 ? ` — lower it by ${slack}` : ''})`)}`);
  return true;
}

/**
 * No generated file may serialize JSON through a reflection-based overload.
 *
 * The generic forms — `ReadFromJsonAsync<T>()`, `JsonSerializer.Deserialize<T>(…, options)` — carry
 * RequiresUnreferencedCode. Under a Native AOT publish they either warn at publish time or, worse,
 * succeed and hand back an object with no fields. Each such call site is invisible while a reflection
 * fallback answers for it: an endpoint that returns `200 {}`, or a query filter that breaks only once it
 * is given typed metadata, is this.
 *
 * This is that measurement, run automatically. It reads the build output, so it must
 * come after buildSolution.
 */
function noReflectionJsonInGeneratedCode() {
  process.stdout.write('  generated code serializes without reflection … ');

  const roots = ['artifacts/build/obj'];
  // `\.Deserialize<` rather than `JsonSerializer.Deserialize<`: the invoke endpoint called
  // `payload.Deserialize<TAction>()` on a JsonElement, which the narrower pattern walked straight past.
  // A check with a blind spot is worse than none — it certifies the area it cannot see.
  const forbidden = /(ReadFromJsonAsync<|\.Deserialize<|JsonSerializer\.(?:Serialize|SerializeToElement|SerializeToNode|SerializeToUtf8Bytes)<)/;
  const offenders = [];

  const walk = (dir) => {
    let entries;
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const entry of entries) {
      const full = join(dir, entry.name);
      if (entry.isDirectory()) { walk(full); continue; }
      if (!entry.name.endsWith('.g.cs')) continue;

      const text = readFileSync(full, 'utf-8');
      const hits = text.split('\n').filter((l) => forbidden.test(l) && !l.trimStart().startsWith('//'));
      if (hits.length > 0) offenders.push({ file: full, count: hits.length, sample: hits[0].trim() });
    }
  };

  for (const root of roots) walk(root);

  if (offenders.length > 0) {
    const total = offenders.reduce((n, o) => n + o.count, 0);
    console.log(bad(`FAIL (${total} call site(s) in ${offenders.length} file(s))`));
    for (const o of offenders.slice(0, 10)) {
      console.log(`    ${o.file}`);
      console.log(`      ${dim(o.sample.slice(0, 140))}`);
    }
    console.log('    Pass a JsonTypeInfo instead. A generated call site knows its type at compile time,');
    console.log('    so there is never a reason for it to resolve one at run time.');
    return false;
  }

  console.log(ok('ok'));
  return true;
}

/**
 * A ratchet on reflection in runtime source, counted from the source itself.
 *
 * This exists because the trim/AOT ratchet above provably cannot see most of it. IL2026/IL3050 measure
 * trim-safety as the linker sees it, and the common reflective patterns — GetProperty on a type the
 * caller already named, GetCustomAttribute, GetInterfaces — produce no warning at all. A
 * module can report ZERO trim warnings and still contain reflection. Two different properties, and
 * each needs its own gate.
 *
 * Like the trim budget this is not zero and is not meant to be: what is left, and why, is listed on
 * REFLECTION_BUDGET. What the ratchet prevents is a new reflective path appearing in a module that has
 * none, unnoticed because no trim warning names it.
 *
 * Source-level and deliberately crude: it counts API names, not reachability. A site that is annotated
 * and unreachable by default still counts. Lowering the number means deleting the call, not hiding it.
 */
function reflectionSourceRatchet() {
  process.stdout.write('  reflection in runtime source … ');

  // Unambiguous: these names belong to System.Reflection and to nothing else we own.
  const unambiguous = [
    /\.GetInterfaces\s*\(/,
    /GetCustomAttributes?\s*[(<]/,
    /Activator\.CreateInstance/,
    /\.MakeGenericType\s*\(|\.MakeGenericMethod\s*\(/,
    /Type\.GetType\s*\(/,
    /Assembly\.Load|AppDomain\.CurrentDomain\.GetAssemblies/,
    /\.GetTypes\s*\(\)|\.GetExportedTypes\s*\(\)/,
    /(?:MethodInfo|PropertyInfo|ConstructorInfo)[^;\n]*\.Invoke\s*\(/,
    /new\s+DefaultJsonTypeInfoResolver\s*\(/,
  ];

  // Ambiguous by name, so they need corroboration on the same line. `entityType.GetProperties()` is
  // EF Core's model metadata and `_parent?.GetProperty(name)` is our own log-context dictionary —
  // neither is reflection, and counting them makes about a quarter of the sites fictional. A genuine
  // reflective member lookup carries BindingFlags or starts from a typeof(...).
  const memberLookup = /\.GetProperty(?:Info)?\s*\(|\.GetProperties\s*\(|\.GetMethods?\s*\(|\.GetFields?\s*\(/;
  const corroborated = /BindingFlags|typeof\s*\(/;

  const countIn = (line) => {
    // A comment naming the API in order to say it is NOT used must not read as a use of it. Several
    // of ours do exactly that: "eliminating GetCustomAttribute reflection at runtime".
    const trimmed = line.trimStart();
    if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) return 0;

    // Same reason, other syntax: the text inside [RequiresUnreferencedCode("Uses
    // Activator.CreateInstance…")] is the declaration of a requirement, not a call.
    const code = line.replace(/"(?:[^"\\]|\\.)*"/g, '""');

    let n = 0;
    for (const re of unambiguous) if (re.test(code)) n++;
    if (memberLookup.test(code) && corroborated.test(code)) n++;
    return n;
  };

  const perProject = new Map();

  const walk = (dir, project) => {
    let entries;
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const entry of entries) {
      if (entry.name === 'obj' || entry.name === 'bin') continue;
      const full = join(dir, entry.name);
      if (entry.isDirectory()) { walk(full, project ?? entry.name); continue; }
      // Generated files are the generator's output, and the generator is measured by what it emits.
      if (!entry.name.endsWith('.cs') || entry.name.endsWith('.g.cs')) continue;

      for (const line of readFileSync(full, 'utf-8').split(String.fromCharCode(10))) {
        const n = countIn(line);
        if (n > 0) perProject.set(project, (perProject.get(project) ?? 0) + n);
      }
    }
  };

  for (const entry of readdirSync('.', { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('Pragmatic.')) continue;
    // src/ only: tests may mock, and a generator runs at compile time where reflection costs nothing.
    for (const project of (() => { try { return readdirSync(join(entry.name, 'src'), { withFileTypes: true }); } catch { return []; } })()) {
      // Two packages are out of scope, both because reflection is what they ARE rather than something
      // they fell back on, and both because an application only gets them by asking:
      //
      //   Migrations.Cli — a command-line tool that inspects the USER's assemblies to find the
      //   generated manifest, exactly as the source generator inspects a compilation. It never runs
      //   inside the application.
      //
      //   Composition.Scanning — convention-based registration, a package of its own so the host
      //   runtime does not carry a scanner it never calls. It cannot be
      //   source-generated: the fluent API takes runtime predicates. Referencing it is the opt-in.
      const outOfScope = ['Pragmatic.Migrations.Cli', 'Pragmatic.Composition.Scanning'];
      if (!project.isDirectory()
        || /SourceGenerator|\.Analyzers$|\.Generator$/.test(project.name)
        || outOfScope.includes(project.name)) continue;
      walk(join(entry.name, 'src', project.name), project.name);
    }
  }

  const total = [...perProject.values()].reduce((a, b) => a + b, 0);

  if (total > REFLECTION_BUDGET) {
    console.log(bad(`FAIL (${total}, budget ${REFLECTION_BUDGET})`));
    for (const [project, count] of [...perProject].sort((a, b) => b[1] - a[1]).slice(0, 10))
      console.log(`    ${project}: ${count}`);
    console.log('    A generated accessor, factory or metadata table knows at compile time what this');
    console.log('    resolves at run time. See docs/CONVENTIONS.md («Reflection») for what is already accepted.');
    return false;
  }

  const slack = REFLECTION_BUDGET - total;
  console.log(`${ok('ok')} ${dim(`(${total}, budget ${REFLECTION_BUDGET}${slack > 0 ? ` — lower it by ${slack}` : ''})`)}`);
  return true;
}

/**
 * How much of the public attribute surface an example application actually writes.
 *
 * A module nobody can be shown is a module that does not exist for a reader, however finished it is.
 * The examples are the only place a consumer sees the framework used, so "is it covered?" is a
 * property of `examples/`, and this is what measures it.
 *
 * ⚠️ It counts ATTRIBUTES, not namespaces and not project references, and that is not a detail:
 * `Pragmatic.Caching` scores zero by namespace while Showcase writes `[Cacheable]` five times,
 * because the attribute arrives through a global using. Three more ways of getting this wrong, each
 * with its case in `scripts/example-coverage.test.mjs`, are recorded in the measure itself.
 *
 * ⚠️ And it is the one number here that must go UP. `node scripts/example-coverage.mjs` prints the
 * names behind it.
 */
function exampleCoverageRatchet() {
  process.stdout.write('  attributes an example writes … ');

  const m = measureCoverage({ repoRoot: '.' });

  if (m.byExample < EXAMPLE_COVERAGE_FLOOR) {
    console.log(bad(`FAIL (${m.byExample} of ${m.total}, floor ${EXAMPLE_COVERAGE_FLOOR})`));
    console.log(`    ${EXAMPLE_COVERAGE_FLOOR - m.byExample} attribute(s) an example used to write are`);
    console.log('    no longer written by one. `node scripts/example-coverage.mjs` names them.');
    console.log('    If the attribute was renamed or removed, lower the floor in the same commit and');
    console.log('    say so; if an example stopped writing it, that is the finding.');
    return false;
  }

  const gained = m.byExample - EXAMPLE_COVERAGE_FLOOR;
  console.log(`${ok('ok')} ${dim(`(${m.byExample}/${m.total}, ${m.byGenerator} by the generator, `
    + `${m.unwritten} by nobody${gained > 0 ? ` — raise the floor by ${gained}` : ''})`)}`);
  return true;
}

/**
 * Every recorded reason still names a proof that exists.
 *
 * ⚠️ The register of reasons says why seven declarations no example writes are decisions
 * rather than gaps, and names the unit-level proof standing in for each. That register is the perfect
 * place for a lie to settle: a shortfall with a sentence beside it reads as answered, and nobody
 * re-reads a sentence. A proof that was renamed, moved or deleted leaves the reason looking settled
 * and resting on nothing — so the names are checked here, where a stale one stops the build.
 *
 * ⚠️ Against the **filesystem**, not `git grep`: measured while building it, a test that exists but is
 * not committed was reported missing, and this tree carries uncommitted work for days at a time.
 */
function recordedReasonsNameRealProofs() {
  process.stdout.write('  a recorded reason names a proof that exists … ');

  const sources = [];
  const walk = dir => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (['obj', 'bin', 'node_modules', 'artifacts', '.git'].includes(entry.name)) continue;
      const path = join(dir, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (entry.name.endsWith('Tests.cs') || entry.name.endsWith('Suite.cs'))
        sources.push(readFileSync(path, 'utf8'));
    }
  };
  walk('.');

  const missing = reasonsWithMissingProofs(name => sources.some(s => s.includes(name)));

  if (missing.length > 0) {
    console.log(bad(`FAIL (${missing.length})`));
    for (const { attribute, proof } of missing)
      console.log(`    ${attribute} → ${proof}`);
    console.log('    The reason still reads as settled and the proof it rests on is gone.');
    console.log('    Point it at the test that replaced it, or take the reason out.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(`(${countedReasons()} reasons, every proof found)`)}`);
  return true;
}

/** How many declarations carry a recorded reason — attributes and packages together. */
function countedReasons() {
  return Object.keys(COVERAGE_REASONS).length + Object.keys(PACKAGE_REASONS).length;
}

/**
 * The coverage measure's own cases.
 *
 * ⚠️ Four false readings were corrected out of this measure before it was trusted, and every one of
 * them looked like a finished number. The cases are the memory of that; without them the next
 * simplification puts one back.
 */
function theCoverageMeasureHolds() {
  return nodeTestCases('scripts/example-coverage.test.mjs', 'the coverage measure holds');
}

/**
 * Attributes the framework declares that nothing reads except a check on how they are written.
 *
 * The class of defect: `[MessageMiddleware]` once registered nothing. Its only reader was a shape diagnostic verifying the class implements the interface, so a middleware declared
 * that way compiled, passed its own check and was never called — and there is nothing to observe from
 * outside, because the messages were still handled, just not wrapped. `ForMessageType` was the same
 * one level down: declared, documented, read by nobody, while the type around it *was* read.
 *
 * ⚠️ **It does not catch a declaration that is read but never reaches what runs**, and that is a
 * finding rather than a gap to close later. `[FromBusinessTimezone]` was read the whole time — by
 * `TemporalConversionModelBinderProvider` at run time and by `TemporalFeature` in the generator — and
 * the defect was that the declaration never reached the generated `{Trigger}Body` record, the type the
 * endpoint deserializes. "Is it read" and "does it reach what runs" are two questions. This counts
 * the first. The second is what an example plus an assertion answers, which is
 * `exampleCoverageRatchet` above.
 *
 * Two numbers, both down only. What counts as read, and the two blind spots kept deliberately, live in
 * `scripts/lib/attribute-readers.mjs`; `node scripts/attribute-readers.mjs` prints the names.
 *
 * ⚠️ **Five false readings were corrected out of this before the number went into a budget**, in four
 * distinct ways, and every one of them printed a confident 3 or 6 — the mistake the reflection ratchet
 * made at 67 with 24% false sites, which made people argue with the measure instead of closing the
 * gap. Two read high (a rule reached through its attribute base; a get-only property read by
 * constructor position) and two read **low**, which is worse: `.endsWith('AttributeNames.cs')` matched
 * the wrong index file, so the real one's 71 declarations were never stripped and everything in it
 * read as used; and an index constant matched as a bare word found the attribute's own name in
 * diagnostic prose. Each correction is a case in the measure's own tests.
 */
function attributeReaderRatchet() {
  process.stdout.write('  attributes nothing reads … ');

  const m = measureAttributeReaders({ repoRoot: '.' });

  if (m.unread.length > UNREAD_ATTRIBUTE_BUDGET || m.unreadProperties.length > UNREAD_PROPERTY_BUDGET) {
    console.log(bad(`FAIL (${m.unread.length} attribute(s), budget ${UNREAD_ATTRIBUTE_BUDGET}; `
      + `${m.unreadProperties.length} propert(y/ies), budget ${UNREAD_PROPERTY_BUDGET})`));
    for (const r of m.unread.slice(0, 10)) console.log(`    ${r.module} ${r.attribute}`);
    for (const p of m.unreadProperties.slice(0, 10)) console.log(`    ${p}`);
    console.log('    A declaration nothing reads compiles, passes its own diagnostic and does nothing.');
    console.log('    Implement it, or remove it and its callers — no [Obsolete] before 1.0: the old goes in');
    console.log('    the same commit. Lower the budget only after the count has genuinely come down.');
    return false;
  }

  const gained = UNREAD_ATTRIBUTE_BUDGET - m.unread.length;
  console.log(`${ok('ok')} ${dim(`(${m.unread.length} attribute(s), budget ${UNREAD_ATTRIBUTE_BUDGET}; `
    + `${m.unreadProperties.length} propert(y/ies), budget ${UNREAD_PROPERTY_BUDGET}`
    + `${gained > 0 ? ` — lower the budget by ${gained}` : ''})`)}`);
  return true;
}

/**
 * The reader measure's own cases.
 *
 * ⚠️ Three of its first six findings were false, in two ways that both read as finished numbers: a
 * validation rule reached through its base class, and a get-only property read by constructor
 * position. Without these cases the next simplification puts them back.
 */
function theReaderMeasureHolds() {
  return nodeTestCases('scripts/attribute-readers.test.mjs', 'the reader measure holds');
}

/**
 * Reflection a TEMPLATE writes into the consumer's application.
 *
 * The ratchet above measures reflection in our runtime source, and it skips two things: generated
 * files, "because the generator is measured by what it emits", and generator projects, "because a
 * generator runs at compile time where reflection costs nothing". Both are reasonable and together
 * they leave a hole exactly where it matters — a string inside a template is neither runtime source
 * nor a .g.cs, and it ends up in every application that uses the feature.
 *
 * It was not hypothetical: [GridAdapter<T>] emitted `typeof(string).GetMethod("Contains")` and
 * `Expression.Property(parameter, "Name")` into the consumer's global grid search, under an attribute
 * documented as producing "compile-time optimized, strongly-typed LINQ expressions".
 *
 * Budget 0, and it starts there. A template knows the type and the member at compile time — that is
 * what a template IS — so a typed lambda always says the same thing.
 *
 * ⚠️ The patterns are narrow on purpose. A first pass counted 12 sites of which 11 were
 * JsonElement.TryGetProperty and an interpolated method name: the same 88%-false-positive mistake the
 * comment in reflectionSourceRatchet records, made again. A member lookup counts only when it starts
 * from a typeof(...), and Expression.Call is excluded because a typed MethodInfo reaches it too.
 */
function emittedReflectionRatchet() {
  process.stdout.write('  reflection templates inject into applications … ');

  const reflective = [
    /typeof\s*\([^)]*\)\s*\.\s*Get(Method|Propert|Field|Member)/,
    /Activator\.CreateInstance/,
    /\.MakeGeneric(Type|Method)/,
    /\.GetInterfaces\s*\(/,
    /GetCustomAttributes?\s*[(<]/,
    /Expression\.(Property|Field)\s*\(/,
  ];

  const sites = [];

  const walk = (dir) => {
    let entries;
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const entry of entries) {
      if (entry.name === 'obj' || entry.name === 'bin') continue;
      const full = join(dir, entry.name);
      if (entry.isDirectory()) { walk(full); continue; }
      if (!entry.name.endsWith('.cs') || entry.name.endsWith('.g.cs')) continue;

      const lines = readFileSync(full, 'utf-8').split(String.fromCharCode(10));
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trimStart();
        if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) continue;
        // Only lines that WRITE code into the output.
        if (!line.includes('AppendLine') && !line.includes('Append(')) continue;
        if (reflective.some((re) => re.test(line)))
          sites.push(`${full}:${i + 1}`);
      }
    }
  };

  walk(join('Pragmatic.SourceGenerator', 'src'));
  walk(join('shared', 'SourceGen'));

  if (sites.length > EMITTED_REFLECTION_BUDGET) {
    console.log(bad(`FAIL (${sites.length}, budget ${EMITTED_REFLECTION_BUDGET})`));
    for (const site of sites.slice(0, 10)) console.log(`    ${site}`);
    console.log('    A template knows the type and the member at compile time. Emit a typed lambda');
    console.log('    instead: the consumer gets the same query with no reflection in their process.');
    return false;
  }

  console.log(`${ok('ok')} ${dim(`(${sites.length}, budget ${EMITTED_REFLECTION_BUDGET})`)}`);
  return true;
}

/**
 * A ratchet on AOT/trimming suppressions in runtime source.
 *
 * The third number, and it exists because the first two provably miss the same code. An
 * [UnconditionalSuppressMessage("AOT", …)] turns off exactly the warning trimWarningRatchet counts, so
 * suppressed code is invisible there by construction. And what it usually suppresses — serializing by
 * `error.GetType()`, resolving a converter for a runtime type — uses no API in the reflection marker
 * list, so it is invisible there too.
 *
 * ErrorJsonHelper is the case that found this: genuinely not AOT-safe, counted by neither ratchet,
 * discovered by reading the file. A suppression is a declaration that something is unsafe; the honest
 * thing is to count the declarations.
 *
 * The budget is AOT_SUPPRESSION_BUDGET.
 */
function aotSuppressionRatchet() {
  process.stdout.write('  AOT suppressions in runtime source … ');

  const perProject = new Map();

  const walk = (dir, project) => {
    let entries;
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const entry of entries) {
      if (entry.name === 'obj' || entry.name === 'bin') continue;
      const full = join(dir, entry.name);
      if (entry.isDirectory()) { walk(full, project); continue; }
      if (!entry.name.endsWith('.cs') || entry.name.endsWith('.g.cs')) continue;

      for (const line of readFileSync(full, 'utf-8').split(String.fromCharCode(10))) {
        const trimmed = line.trimStart();
        if (trimmed.startsWith('//') || trimmed.startsWith('*')) continue;
        if (/UnconditionalSuppressMessage\s*\(\s*"(AOT|Trimming|SingleFile)"/.test(line))
          perProject.set(project, (perProject.get(project) ?? 0) + 1);
      }
    }
  };

  for (const entry of readdirSync('.', { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('Pragmatic.')) continue;
    for (const project of (() => { try { return readdirSync(join(entry.name, 'src'), { withFileTypes: true }); } catch { return []; } })()) {
      if (!project.isDirectory() || /SourceGenerator|\.Analyzers$|\.Generator$/.test(project.name)) continue;
      walk(join(entry.name, 'src', project.name), project.name);
    }
  }

  const total = [...perProject.values()].reduce((a, b) => a + b, 0);

  if (total > AOT_SUPPRESSION_BUDGET) {
    console.log(bad(`FAIL (${total}, budget ${AOT_SUPPRESSION_BUDGET})`));
    for (const [project, count] of [...perProject].sort((a, b) => b[1] - a[1]).slice(0, 10))
      console.log(`    ${project}: ${count}`);
    console.log('    Silencing the warning does not make the code AOT-safe. Give the call a JsonTypeInfo,');
    console.log('    a generated accessor or a typed switch — or let the warning stand and be counted.');
    return false;
  }

  const slack = AOT_SUPPRESSION_BUDGET - total;
  console.log(`${ok('ok')} ${dim(`(${total}, budget ${AOT_SUPPRESSION_BUDGET}${slack > 0 ? ` — lower it by ${slack}` : ''})`)}`);
  return true;
}

/**
 * Publishes the AOT smokes and runs them.
 *
 * The only signal that answers "does this actually work when published Native AOT". A clean build
 * does not: the whole solution can compile with every test green while a generated endpoint published
 * AOT returns 500 — or 200 with an empty body, which is worse. Left as manual smokes, that stays true
 * for months.
 *
 * Needs the native toolchain, and says so rather than passing quietly: a skip that looks like a pass
 * is the failure mode this whole exercise was about.
 */
async function aotSmokes() {
  const scripts = [
    'examples/aot-smoke/publish-and-smoke.ps1',
    'examples/aot-smoke/publish-and-smoke-generated.ps1',
    'examples/aot-smoke/publish-and-smoke-web.ps1',
  ];

  let good = true;
  for (const script of scripts) {
    process.stdout.write(`  ${basename(script)} … `);
    const { code, out, spawnError } = run('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script]);

    if (spawnError) {
      console.log(bad(`COULD NOT RUN — ${spawnError}`));
      good = false;
      continue;
    }

    if (code !== 0) {
      console.log(bad(`FAIL (exit ${code})`));
      const lines = out.split('\n').filter((l) => /FAIL|error|Exception|not recognized/.test(l));
      console.log(lines.slice(0, 8).join('\n'));
      good = false;
      continue;
    }

    console.log(ok('ok'));
  }

  return good;
}

/**
 * Whether the native toolchain is present.
 *
 * On Windows the ILC link step shells out to vswhere for the MSVC linker. The smoke scripts put the
 * installer directory on PATH themselves, so the question here is only whether it is installed at
 * all — a machine without Visual Studio's C++ workload cannot run these, and saying so beats a
 * failure at the last step of a five-minute publish.
 */
function nativeToolchainAvailable() {
  if (process.platform !== 'win32') return true;

  return existsSync('C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe')
    || run('where', ['vswhere']).code === 0;
}

function dockerAvailable() {
  return run('docker', ['info', '--format', '{{.ServerVersion}}']).code === 0;
}

/**
 * Reclaims stopped containers and dangling volumes between container suites.
 *
 * Serialising the suites removed the saturation from running them all at once, but not the
 * accumulation: on a full run the daemon still degraded after about seven suites, and the last four
 * failed on container start-up while every hermetic test in them passed. Testcontainers removes its
 * own containers via Ryuk, but not always promptly, and the leftovers add up across eleven suites.
 *
 * ⚠️ **Only what Testcontainers created**, by label. An unfiltered prune is argued safe — "only
 * stopped containers and unused volumes, so a container someone else is running is not touched" —
 * and that is true of *running* containers and of nothing else: another project's stopped containers
 * and its unused volumes go with them. On a contributor's machine that includes an unrelated
 * project's compose stack and **bagetter** — the local NuGet feed this repository's own how-to tells
 * contributors to run. Stop it for a moment and an unfiltered prune deletes it.
 *
 * ⚠️ What that costs: anonymous volumes created by a pruned container's image carry no label, so they
 * are not swept. If the daemon degradation this function exists for comes back, that is where
 * to look — and the measurement to take is the one in the note above, not a wider prune.
 */
function pruneBetweenSuites() {
  run('docker', ['container', 'prune', '-f', '--filter', `label=${TESTCONTAINERS_LABEL}`]);
  run('docker', ['volume', 'prune', '-f', '--filter', `label=${TESTCONTAINERS_LABEL}`]);
}


/**
 * Whether the daemon still answers. A container suite that fails while Docker is unreachable says
 * nothing about the code, and reporting it as a code failure is how a gate loses its meaning.
 */
function dockerStillAlive() {
  return dockerAvailable();
}

async function runSuites(projects, { parallel, containerTier = false, poolSize = jobs }) {
  const results = [];

  // How crowded the machine was, sampled around each suite. A red that does not reproduce alone is
  // diagnosed from the run that produced it or not at all, and the first question about one is whether
  // it was alone. Two samples rather than a running peak: enough to separate "it failed while thirteen
  // suites were running" from "it failed with the machine to itself", and free.
  let inFlight = 0;

  // ⚠️ Asynchronous, and that is the whole point. With the synchronous `run()`, the worker pool below
  // would fan out N `async` functions that each block the event loop: the second suite could not
  // start until the first returned, and `--jobs` would parallelise nothing.
  const runOne = async (proj) => {
    // ⚠️ The logger is detailed, the build stays quiet. The failure reason is only in the output at
    // detailed test verbosity, and parseFailedTests looks for it there: with the default logger it
    // finds names and nothing else, so every red the gate reports is a name the reader has to
    // reproduce to understand — and a flake does not reproduce. `-v n` would also produce it, and
    // brings the whole MSBuild log with it; this asks the test logger alone. The extra text is
    // captured, never printed: the gate still prints only its own lines.
    const { out } = await runAsync('dotnet',
      ['test', proj, '--nologo', '-v', 'q', '--logger', 'console;verbosity=detailed',
        '--no-build', ...ARTIFACTS]);
    return { proj, ...parseTestOutput(out), out };
  };

  // Every line a suite produces, held until the suite is done and then printed together. With the
  // suites genuinely overlapping, printing as you go interleaves two reports into one unreadable
  // column — and the failure lines, which are the reason the detail is printed at all, are exactly
  // what would be split.
  const one = async (proj) => {
    const startedAt = performance.now();
    const lines = [];
    const console = { log: (line = '') => lines.push(line) };
    const crowdAtStart = ++inFlight;
    let r;
    try {
      r = await runOne(proj);
    } finally {
      inFlight--;
    }
    const crowdAtEnd = inFlight + 1;
    const name = basename(proj, '.csproj');

    // A suite that executed nothing gets one serial confirmation before it is believed.
    //
    // The guard downstream reads "0 tests where the last run had 15" as "the suite did not run", which
    // is the right reading and the reason it is enforced. What it cannot see is WHY, and under this
    // parallel fan-out one of the reasons is a testhost that did not start — a suite that runs 15/15
    // in isolation can report zero while its neighbours are green. Failing the gate
    // on that turns a scheduling hiccup into a red build, and a gate that goes red at random is no more
    // usable than one that goes green at random.
    //
    // The retry costs one `dotnet test` on a suite that already produced nothing, and it is what makes
    // the two explanations distinguishable: a suite that is genuinely empty reports zero twice.
    if (!r.buildError && r.passed + r.failed + r.skipped === 0) {
      console.log(`  ${dim('...')}    ${name} — ran no tests, confirming`);
      r = await runOne(proj);
    }

    if (r.buildError) console.log(`  ${bad('BUILD')}  ${name}`);
    else if (r.failed > 0) {
      console.log(`  ${bad('FAIL')}   ${name} — ${r.failed} failed, ${r.passed} passed ${dim(took(startedAt))}`);
      // Name the tests, not just the suite: a failure that does not reproduce in isolation cannot be
      // diagnosed from a re-run, so the only chance to see it is the run that produced it.
      for (const t of r.failures.slice(0, 10)) console.log(`           ${dim('↳')} ${t}`);
      if (r.failures.length > 10) console.log(`           ${dim(`↳ … and ${r.failures.length - 10} more`)}`);
      console.log(`           ${dim(`↳ pool: ${crowdAtStart} running at start, ${crowdAtEnd} at finish, of ${poolSize}`)}`);
    }
    else if (r.noTests) console.log(`  ${dim('none')}   ${name}`);
    // A suite that executed nothing is not a pass, whatever the reason. Printing "pass — 0" and then
    // failing the run two lines later over the same suite is a contradiction the reader has to
    // untangle, and the "pass" is the half they remember.
    else if (r.passed + r.failed + r.skipped === 0) console.log(`  ${dim('none')}   ${name}`);
    else console.log(`  ${ok('pass')}   ${name} — ${r.passed}${r.skipped ? ` (+${r.skipped} skipped)` : ''} ${dim(took(startedAt))}`);

    for (const line of lines) globalThis.console.log(line);
    return r;
  };

  if (!parallel) {
    for (const proj of projects) {
      const r = await one(proj);
      results.push(r);

      // A failing container suite is only evidence about the code if the daemon is still up. Checking
      // here turns "4 suites red" into "Docker died", which is a different problem with a different fix.
      if (r.failed > 0 && containerTier && !dockerStillAlive()) {
        r.dockerDied = true;
        console.log(bad('  Docker stopped responding — remaining container suites cannot be judged.'));
        break;
      }

      if (containerTier) pruneBetweenSuites();
      if ((r.failed > 0 || r.buildError) && !keepGoing) break;
    }
    return results;
  }

  // In the order they were given, not the order they finished: a report that reshuffles itself run
  // to run cannot be compared with yesterday's, which is most of what this output is for.
  results.push(...await runPool(projects.map((proj) => () => one(proj)), poolSize));
  return results;
}

/**
 * The container suites, at the pace `--docker-jobs` asks for.
 *
 * ⚠️ In **waves**, not one open pool, because pruning is the thing that cannot overlap. Reclaiming
 * stopped containers and unused volumes is what keeps the daemon healthy across a long tier, and it
 * cannot run beside a suite that is starting one — an unused volume is also a volume nobody has
 * attached *yet*. So each wave drains before the next begins, and the prune happens in the gap.
 *
 * The cost of a barrier is a wave that waits for its slowest member. That is the price of keeping the
 * prune cadence, and it is visible in the tier's own total rather than hidden.
 */
async function runContainerSuites(projects) {
  if (dockerJobs === 1)
    return runSuites(projects, { parallel: false, containerTier: true });

  const results = [];

  for (let i = 0; i < projects.length; i += dockerJobs) {
    const wave = projects.slice(i, i + dockerJobs);
    results.push(...await runSuites(wave, { parallel: true, containerTier: true, poolSize: dockerJobs }));
    pruneBetweenSuites();

    if (results.some((r) => r.failed > 0 || r.buildError) && !keepGoing) break;
  }

  return results;
}

/**
 * One gate run at a time, and a stale lock does not wedge the next one.
 *
 * ⚠️ Guarded because the failure it prevents is the one that costs the most: two runs sharing
 * artifacts/build produce a wreck that reads as a code failure, and a gate whose red means "maybe
 * something else was running" is not a gate. Run as a node test for the same reason the other two
 * are — the thing at risk is this script.
 */
function oneRunAtATime() {
  const test = 'scripts/run-lock.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  one gate run at a time … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/** How the container tier is about to run, said out loud so a timing is never read against the wrong pace. */
function dockerPace() {
  return dockerJobs === 1 ? 'one at a time' : `${dockerJobs} at a time, in waves`;
}

// ── CI path verification ─────────────────────────────────────────────────────

/**
 * Anything CI names must exist. Two jobs pointed at solution files that were never there; because they
 * only ran on pull_request they never failed loudly enough for anyone to notice.
 */
function verifyPaths() {
  const dir = '.github/workflows';
  if (!existsSync(dir)) return true;
  let bad_ = 0;
  for (const f of readdirSync(dir).filter((f) => f.endsWith('.yml'))) {
    const text = readFileSync(join(dir, f), 'utf8');

    // Literal paths.
    for (const m of text.matchAll(/[\w./-]+\.slnx?\b/g)) {
      const p = m[0];
      if (!existsSync(p)) {
        console.log(`  ${bad('MISSING')} ${p}  ${dim(`(referenced by ${f})`)}`);
        bad_++;
      }
    }

    // Paths built from a matrix, e.g. ${{ matrix.module }}/${{ matrix.module }}.slnx. Skipping these
    // is how a dead job hides: expand the declared values and check each one.
    if (/\$\{\{\s*matrix\.module\s*\}\}[^\n]*\.slnx/.test(text)) {
      const block = text.match(/module:\s*\n((?:\s*-\s*[\w.]+\s*\n)+)/);
      for (const line of block?.[1].split('\n') ?? []) {
        const mod = line.match(/-\s*([\w.]+)/)?.[1];
        if (!mod) continue;
        const p = `${mod}/${mod}.slnx`;
        if (!existsSync(p)) {
          console.log(`  ${bad('MISSING')} ${p}  ${dim(`(matrix entry '${mod}' in ${f})`)}`);
          bad_++;
        }
      }
    }
  }
  if (bad_ === 0) console.log(`  ${ok('ok')} every solution path referenced by CI exists`);
  return bad_ === 0 && docsInSync() && skillExamplesInSync() && skillsArePortable() && everyModuleHasALicense();
}

/**
 * The pages under `site/docs` are generated from each module's `docs/`, and nothing compared them.
 * Correcting twenty source documents left twenty published copies stale, and no build, test or gate
 * said so — the same shape as every "someone has to remember to regenerate it" defect this gate
 * exists to catch. `--check` regenerates into memory and changes nothing.
 */
/**
 * The site-reference check counts rows, not mentions.
 *
 * ⚠️ Run as a node test: `--check` passing says every ID appears, never that «appears» means
 * documented. The page names IDs in its prose and in its range
 * table, and a check that counted those would call a diagnostic documented because another row cites
 * it — the exact way this gap stayed invisible while the page looked complete.
 */
function siteDiagnosticsCheckCountsRows() {
  const test = 'scripts/site-diagnostics.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the site-reference check counts rows, not mentions … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The suite runner overlaps when it is asked to, and bounds itself when it is not.
 *
 * ⚠️ Guarded here because the failure is invisible from the outside: a scheduler that silently
 * serialises produces correct results, in a plausible order, with a total nobody has a prediction
 * for. That is how `--jobs` stayed inert and documented for as long as it did. Run as a node test
 * because the thing at risk is the script itself.
 */
/**
 * A red run leaves a record behind.
 *
 * ⚠️ Tested here, with the other two script self-tests, for the same reason they are: the thing at
 * risk is this script. A gate whose only account of a failure is the terminal it printed to teaches
 * people to re-run instead of read, and a re-run of a flake comes back green and proves nothing.
 */
function failureRecordWorks() {
  const test = 'scripts/failure-record.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  a red run leaves a record … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * A clean vulnerability verdict stops being reusable.
 *
 * ⚠️ The scan is keyed on the dependency graph, and an advisory is published against a graph that did
 * not move — which is the one case the key cannot see. `GHSA-23fw-v26w-5fgq` was live against a
 * package every runtime library reached transitively, and it surfaced only when an unrelated
 * `ProjectReference` moved the graph for another story. The calendar is the second key, and it was
 * written without anything measuring it: deleting the age check failed nothing.
 */
function vulnerabilityVerdictExpires() {
  const test = 'scripts/supply-chain.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  a clean scan stops being reusable … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * Generated code never names the wall clock.
 *
 * ⚠️ A `DateTimeOffset.UtcNow` written into generated source is the one time source no container can
 * replace: it produces a row whose `CreatedAt` obeys an application's pinned clock while its
 * `DeletedAt` does not. The sites that need the seam are the mutation invoker's soft delete, the saga
 * orchestrator's `StartedAt`, and the repository filling `FilterContext.Now`, which is `required`
 * precisely so it can be pinned.
 *
 * The accepted form is `timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow` — an
 * injected clock with a fallback — so this counts only the emissions that offer no such seam. Budget
 * zero: there is no reason for the next one.
 */
function generatedCodeNamesNoWallClock() {
  const roots = ['Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Features'];
  const offenders = [];

  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'obj' || entry.name === 'bin') continue;
      const p = join(dir, entry.name);
      if (entry.isDirectory()) { walk(p); continue; }
      if (!entry.name.endsWith('.cs')) continue;

      const lines = readFileSync(p, 'utf8').split('\n');
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        // An emission, not a mention: the text has to be going into generated source.
        if (!/Append(Line)?\(\$?"/.test(line)) continue;
        if (!/DateTimeOffset\.UtcNow|DateTime\.UtcNow/.test(line)) continue;
        // The accepted shape offers an injected clock first.
        if (/timeProvider\?\./.test(line)) continue;
        offenders.push(`${p}:${i + 1}`);
      }
    }
  };

  process.stdout.write('  generated code names no wall clock … ');
  for (const root of roots) if (existsSync(root)) walk(root);

  if (offenders.length === 0) {
    console.log(`${ok('ok')} ${dim('(0, budget 0)')}`);
    return true;
  }

  console.log(bad(`FAIL (${offenders.length}, budget 0)`));
  for (const site of offenders.slice(0, 10)) console.log(`  ${site}`);
  console.log(dim('  emit timeProvider?.GetUtcNow() ?? … and inject the clock, as the repository does'));
  return false;
}

/**
 * The documentation shows no generic `[Entity<…>]`.
 *
 * ⚠️ `EntityAttribute` has no type parameter — the key is always a Guid v7 — and the generic form is
 * `CS0308`. Skills or XML docs showing it as the form to copy cost a consumer a build round, with the
 * answer only in the attribute's own doc. A rename that leaves its old form in the documentation is the pattern; this makes the next one
 * visible.
 *
 * Counted: `[Entity<`, `IEntity<`, their XML-escaped spelling, and a repository with two type arguments
 * (`IRepository<T, TId>`, gone with the same rename). Where: the skills, the published site,
 * the root and module READMEs, every module's `docs/`, and the comments (`//` and `///`) of every
 * module's `src/` and `samples/`. Tests are not read: a test may name the old form as the input it
 * proves is refused. Sentences that tell the story of the removal are allowed by name below. Budget
 * zero.
 */
function docsNameNoGenericEntity() {
  const offenders = [];
  // `(?!\/)`: `<c>IEntity</c>` is the interface in an XML doc, not a type argument.
  const form = /\[Entity<(?!\/)|\bIEntity<(?!\/)|\bIEntity&lt;|\[Entity&lt;|\bI(Read)?Repository(<|&lt;)\w+,\s*\w+(>|&gt;)/;

  // Sentences that tell the story of the removal. They name the old form on purpose, and nobody
  // copies a sentence that says the form was taken away. Named one by one, with the text they must
  // contain, so a new offender in the same file is still counted.
  const history = [
    ['EntityAttribute.cs', 'took a type parameter'],
    ['IEntity.cs', 'which read as a choice'],
    ['IEntity.cs', 'still had a <c>Guid PersistenceId</c>'],
    ['IEntity.cs', 'Measured before removing it'],
    ['MissingEntityTraitTransform.cs', 'disagree in both directions'],
  ];
  const isHistory = (path, line) =>
    history.some(([file, text]) => path.endsWith(file) && line.includes(text));

  const walk = (dir, accept, test) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'obj' || entry.name === 'bin' || entry.name === 'node_modules') continue;
      const p = join(dir, entry.name);
      if (entry.isDirectory()) { walk(p, accept, test); continue; }
      if (!accept(entry.name)) continue;
      const lines = readFileSync(p, 'utf8').split('\n');
      for (let i = 0; i < lines.length; i++)
        if (test(lines[i]) && !isHistory(p, lines[i])) offenders.push(`${p}:${i + 1}`);
    }
  };
  const markdown = (n) => n.endsWith('.md') || n.endsWith('.mdx');
  const prose = (line) => form.test(line);
  const comment = (line) => line.trimStart().startsWith('//') && form.test(line);

  process.stdout.write('  the documentation shows no generic [Entity<…>] … ');

  for (const dir of ['marketplace', 'site/docs/src/content/docs'])
    if (existsSync(dir)) walk(dir, markdown, prose);
  const readmes = ['README.md'];
  for (const module of readdirSync('.', { withFileTypes: true })) {
    if (!module.isDirectory() || !module.name.startsWith('Pragmatic.')) continue;
    readmes.push(join(module.name, 'README.md'));
    for (const [sub, accept, test] of [['docs', markdown, prose], ['src', (n) => n.endsWith('.cs'), comment], ['samples', (n) => n.endsWith('.cs'), comment]]) {
      const dir = join(module.name, sub);
      if (existsSync(dir)) walk(dir, accept, test);
    }
  }
  for (const readme of readmes.filter((r) => existsSync(r)))
    for (const [i, line] of readFileSync(readme, 'utf8').split('\n').entries())
      if (prose(line)) offenders.push(`${readme}:${i + 1}`);

  if (offenders.length === 0) {
    console.log(`${ok('ok')} ${dim('(0, budget 0)')}`);
    return true;
  }

  console.log(bad(`FAIL (${offenders.length}, budget 0)`));
  for (const site of offenders.slice(0, 10)) console.log(`  ${site}`);
  console.log(dim('  [Entity], IEntity and IRepository<T> take no key type: the key is always a Guid'));
  return false;
}

/**
 * The gate leaves no build servers behind.
 *
 * ⚠️ Deleting the one line that disables node reuse fails nothing otherwise, and what it costs is
 * invisible: the next run is slower and quietly does less. See the file for what this checks and what
 * it deliberately does not.
 */
function gateLeavesNoProcesses() {
  const test = 'scripts/gate-processes.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the gate leaves no build servers behind … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The gate deletes only what belongs to this checkout.
 *
 * ⚠️ Dropping the `--filter` from a prune, or going back to killing test hosts by name, costs nothing
 * on CI and takes somebody else's containers and test run on a developer machine — which is where
 * AGENTS.md sends a contributor to run this. Nothing else would notice.
 */
function gateCleansOnlyItsOwn() {
  const test = 'scripts/gate-ownership.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the gate cleans only what is its own … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The path somebody else arrives by still works.
 *
 * ⚠️ What this holds is what drifts without a sound: a machine-specific path in the publish script,
 * and consumer samples asking for a version nothing publishes — which restores cleanly from nuget.org
 * and proves nothing about the packages the samples exist to prove.
 */
function theFirstRunHolds() {
  const test = 'scripts/first-run.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the first run from a clean clone holds … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The check publish-local runs on every package before pushing it: every relative link in a package's
 * markdown resolves inside the package. See the test file for what it covers and what it does not.
 */
function packageLinksAreChecked() {
  const test = 'scripts/package-markdown-links.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  package markdown links are checked … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The native binaries in each module's runtimes/ (PDF, Imaging) against the Rust source they come
 * from. Each carries the hash of its crate's source (scripts/native-stamp.mjs); a stale or unstamped
 * one fails here. ⚠️ The PDF win-x64 DLL once shipped for five months without the engine's last change,
 * and every test stayed green: no test reads a table out of a PDF. The self-test runs
 * first, so a broken hash cannot pass for a current binary.
 */
function nativeBinariesAreCurrent() {
  const test = 'scripts/native-stamp.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  native binaries built from the current source … ');
  const self = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });
  if (self.status !== 0) {
    console.log(bad('FAIL'));
    for (const line of `${self.stdout || ''}${self.stderr || ''}`.split('\n'))
      if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
    return false;
  }

  const r = spawnSync(process.execPath, ['scripts/native-stamp.mjs', 'check'], { encoding: 'utf8' });
  if (r.status === 0) {
    console.log(`${ok('ok')} ${dim(`(${(r.stdout || '').trim()})`)}`);
    return true;
  }

  console.log(bad('STALE'));
  for (const line of (r.stderr || '').split('\n'))
    if (line.trim()) console.log(`  ${line.trimEnd()}`);
  return false;
}

/**
 *     The reading behind the undeclared-contracts ratchet.
 *
 * A ratchet is only as good as what it counts, and this one counts by name: the cases that matter are
 * the ones where "registered" and "declared" come apart, plus the control that keeps it from being
 * satisfied by annotating every interface in the repository.
 */
function contractsRatchetReadsWhatItClaims() {
  const test = 'scripts/host-provided-contracts.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the undeclared-contracts reading is measured … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The rule behind the silent-drop number, on a few lines rather than on the tree.
 *
 * ⚠️ The counter counted the one file whose whole job is to report: a transform that answers with a
 * value-equatable carrier instead of constructing a `Diagnostic` — which it cannot do, because a
 * `Diagnostic` in an incremental pipeline breaks caching — read as silence, and its five sites took
 * the budget to exactly full. A ratchet nobody can check is a ratchet people fight.
 */
function silentDropRuleHolds() {
  return nodeTestCases('scripts/silent-drops.test.mjs', 'the silent-drop rule knows a carrier');
}

function suiteRunnerOverlaps() {
  return nodeTestCases('scripts/spawn-async.test.mjs', 'the suite runner overlaps when asked');
}

/** Runs a `node --test` file and reports its case count, or its failures. */
function nodeTestCases(test, label) {
  if (!existsSync(test)) return true;

  process.stdout.write(`  ${label} … `);
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * The diagnostics dictionary against the sources it is generated from.
 *
 * ⚠️ `sync-diagnostics.mjs` has a `--check` mode written for exactly this, and a gate that does not
 * run it stays green while descriptors are added to the codebase without a sync. The dictionary is
 * what a developer searches when they hit a PRAG number — a stale one answers «no such
 * diagnostic» for a diagnostic that exists.
 */
function diagnosticsInSync() {
  const script = 'scripts/sync-diagnostics.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  diagnostics dictionary in sync … ');
  const r = spawnSync(process.execPath, [script, '--check'], { encoding: 'utf8' });

  if (r.status === 0) {
    console.log(ok('ok'));
    return true;
  }

  console.log(bad('DRIFTED'));
  for (const line of `${r.stderr || ''}${r.stdout || ''}`.split('\n'))
    if (line.trim()) console.log(`    ${line.trim()}`);
  console.log('    Fix: node scripts/sync-diagnostics.mjs');
  return false;
}

/**
 * The published diagnostics reference against the dictionary generated from the descriptors.
 *
 * ⚠️ `diagnosticsInSync` guards the dictionary, which is generated and therefore cannot fall behind.
 * The page a user reads is written by hand — it says what the number means and what to do, which no
 * descriptor holds — and nothing else connects the two: without this check the page falls behind by
 * whole ranges, a module's diagnostics at a time, without a word. A reference that answers «no such diagnostic» for one the build just emitted sends the reader looking
 * for a typo in their own code.
 */
function siteListsEveryDiagnostic() {
  const script = 'scripts/site-diagnostics.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  every diagnostic has a row on the site … ');
  const r = spawnSync(process.execPath, [script, '--check'], { encoding: 'utf8' });

  if (r.status === 0) {
    const count = /ok (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${count} declared)`)}`);
    return true;
  }

  console.log(bad('BEHIND'));
  for (const line of `${r.stderr || ''}${r.stdout || ''}`.split('\n'))
    if (line.trim()) console.log(`    ${line.trim()}`);
  return false;
}

/**
 * The site sync resolves a link it cannot serve from the document that wrote it. Taken from the
 * repository root, a module's `../samples/X` would name a folder that is not there, and a sibling doc
 * written bare would stay relative to the page URL: hundreds of links on the published site would
 * name nothing.
 */
function siteLinksResolveFromTheirSource() {
  const test = 'site/scripts/sync-docs.test.mjs';
  if (!existsSync(test)) return true;

  process.stdout.write('  the site sync resolves links from their source … ');
  const r = spawnSync(process.execPath, ['--test', test], { encoding: 'utf8' });

  if (r.status === 0) {
    const pass = /# pass (\d+)/.exec(r.stdout || '')?.[1] ?? '?';
    console.log(`${ok('ok')} ${dim(`(${pass} cases)`)}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of `${r.stdout || ''}${r.stderr || ''}`.split('\n'))
    if (/^not ok|Error|expected|actual/.test(line.trim())) console.log(`  ${line.trim()}`);
  return false;
}

function docsInSync() {
  const script = 'site/scripts/sync-docs.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  docs site in sync … ');
  const r = spawnSync(process.execPath, [script, '--check'], { encoding: 'utf8' });

  if (r.status === 0) {
    console.log(ok('ok'));
    return true;
  }

  console.log(bad('DRIFTED'));
  // Only the drift lines: the script also logs one line per module on its way there.
  for (const line of (r.stderr || r.stdout || '').split('\n')) {
    if (/^\s{3}(changed|missing|orphaned)\s/.test(line) || /^\s+Fix:/.test(line))
      console.log(`  ${line.trim()}`);
  }
  return false;
}

/**
 * The examples a skill ships against the tested code they are copied from.
 *
 * A skill is read outside this repository, so its examples live in its folder — and one written by hand
 * ages like prose, with nothing compiling or running it. These are copies of files the repository builds
 * and a named suite exercises; the check is what keeps "copied from tested code" true after the source
 * moves on.
 */
function skillExamplesInSync() {
  const script = 'scripts/sync-skill-examples.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  skill examples in sync with their sources … ');
  const r = spawnSync(process.execPath, [script, '--check'], { encoding: 'utf8' });

  if (r.status === 0) {
    console.log(`${ok('ok')} ${dim((r.stdout || '').trim().replace(/^skill examples in sync /, ''))}`);
    return true;
  }

  console.log(bad('DRIFTED'));
  for (const line of (r.stdout || r.stderr || '').split('\n'))
    if (line.startsWith('  ')) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * Every skill reads the same in every agent that loads SKILL.md, not only in Claude Code.
 *
 * Claude Code forgives a byte-order mark before the frontmatter and reads `when_to_use`; Codex drops
 * the first skill silently and ignores the second field. Nothing in a Claude Code session shows either,
 * so the rule is checked here (skill-portability.mjs).
 */
function skillsArePortable() {
  const script = 'scripts/skill-portability.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  skills portable across agents … ');
  const r = spawnSync(process.execPath, [script], { encoding: 'utf8' });

  if (r.status === 0) {
    console.log(`${ok('ok')} ${dim((r.stdout || '').trim().replace(/^skills portable /, ''))}`);
    return true;
  }

  console.log(bad('NOT PORTABLE'));
  for (const line of (r.stdout || r.stderr || '').split('\n'))
    if (line.startsWith('  ')) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * Every sample project, run from the assemblies the clean build just produced; each must exit 0.
 *
 * The build compiles the samples and nothing else runs them: without this, a sample broken by a change
 * to its module stays broken until somebody opens it. The samples that cannot run here are named, with the reason,
 * in scripts/samples-excluded.json.
 */
function samplesRun() {
  const script = 'scripts/run-samples.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  every sample runs to exit 0 … ');
  const r = spawnSync(process.execPath, [script], { encoding: 'utf8' });
  const summary = /samples: .*/.exec(r.stdout || '')?.[0] ?? '';

  if (r.status === 0) {
    console.log(`${ok('ok')} ${dim(summary.replace(/^samples: /, ''))}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of (r.stdout || r.stderr || '').split('\n'))
    if (line.startsWith('  FAIL') || line.startsWith('          ')) console.log(`  ${line.trim()}`);
  return false;
}

/**
 * Every module is named in docs/LICENSING.md. The packaging falls back to PolyForm for a module that is
 * not on the free list, so without this a new module ships under a license nobody chose.
 */
function everyModuleHasALicense() {
  const script = 'scripts/license-map.mjs';
  if (!existsSync(script)) return true;

  process.stdout.write('  every module has a declared license … ');
  const r = spawnSync(process.execPath, [script], { encoding: 'utf8' });

  if (r.status === 0) {
    console.log(`${ok('ok')} ${dim((r.stdout || '').trim().replace(/^every module has a declared license /, ''))}`);
    return true;
  }

  console.log(bad('FAIL'));
  for (const line of (r.stdout || r.stderr || '').split('\n'))
    if (line.startsWith('  ')) console.log(`  ${line.trim()}`);
  return false;
}

// ── environment ──────────────────────────────────────────────────────────────

/**
 * Kills test hosts left over from an earlier run.
 *
 * Isolated output (see ARTIFACTS) stops the gate from fighting an IDE over bin/, but it does not
 * stop the gate from fighting ITSELF: a testhost that outlives its suite holds files inside
 * artifacts/build, and the next clean build fails with MSB3061 on a project nobody touched. That
 * was mistaken for the isolation being enough — it is not, the two problems are different.
 *
 * Only test hosts. MSBuild nodes and VBCSCompiler are reusable build infrastructure; killing those
 * each run would slow every build and could cut off a compile in progress.
 */
function clearOrphanedTestHosts() {
  const mine = ownTestHostPids();

  for (const { pid, name } of mine) {
    const kill = process.platform === 'win32'
      ? run('taskkill', ['/PID', String(pid), '/F'])
      : run('kill', ['-9', String(pid)]);
    if (kill.code === 0) console.log(`  ${dim(`cleared an orphaned ${name} (pid ${pid})`)}`);
  }
}

/**
 * Test hosts whose command line names this repository — the only ones this gate may kill.
 *
 * ⚠️ Not `taskkill /IM testhost.exe /F`: that is every test host on the machine, including the one
 * an IDE started a second earlier on somebody else's solution — calling them "orphaned" establishes
 * nothing of the sort. A process running this repository's suites carries
 * the repository path on its command line; one running another solution does not, which is the
 * ownership this can actually read.
 *
 * ⚠️ It reads the command line and not the parent: the hosts worth clearing are left over from an
 * earlier run whose parent is already gone, so "descends from me" would find none of them.
 */
function ownTestHostPids() {
  const names = ['testhost', 'vstest.console'];
  const listing = process.platform === 'win32'
    ? run('powershell', ['-NoProfile', '-Command',
        "Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'testhost|vstest.console' } " +
        "| ForEach-Object { \"$($_.ProcessId)`t$($_.Name)`t$($_.CommandLine)\" }"])
    : run('ps', ['-eo', 'pid=,comm=,args=']);

  if (listing.code !== 0) return [];

  return ownTestHosts(listing.out, {
    windows: process.platform === 'win32',
    repoRoot: process.cwd(),
    names,
  });
}

// ── test-count baseline ──────────────────────────────────────────────────────

const COUNTS_FILE = 'artifacts/gate-counts.json';

/**
 * The trim/AOT warning budget for Pragmatic's own assemblies. Lower it whenever the count comes down;
 * never raise it to make a build pass.
 *
 * The one legitimate reason it moves up: every entity brings a generated repository whose
 * `AsQueryable` is IL2026 + IL3050, so a package that starts owning entities adds that surface —
 * `Pragmatic.Authorization.Management`'s five are ten of the warnings counted here. That is surface
 * arriving, not a regression in code that already existed, and it is said in the commit that raises it.
 */
const TRIM_WARNING_BUDGET = 194;

/**
 * A ratchet on the OTHER trim family: warnings that say a reflective requirement is not DECLARED.
 *
 * IL2026/IL3050 above mean "this code needs unreferenced or dynamic code" — the requirement is stated,
 * and the ratchet keeps new ones from arriving unnoticed. IL2060/2070/2072/2075/2087/2090/2091 mean
 * something different and, for this repository, worse: a requirement exists and does NOT propagate to
 * the caller, so a type gets trimmed away and the reflection that needed it fails at runtime with
 * nothing at build time having said so. That is precisely what the rule about annotating unavoidable
 * reflection is for, and this is the signal that measures it.
 *
 * At 17: Persistence.EFCore 6, Migrations.Cli 5, Temporal.EFCore 3, Migrations / Configuration /
 * Composition.Host 1 each. Falls, never rises.
 */
const UNDECLARED_REFLECTION_BUDGET = 17;

/**
 * Reflection call sites in runtime source.
 *
 * ⚠️ A site filed under "EF model building, the sanctioned exception" is read, not taken on the word
 * of a comment saying so: a scan over a closed set of types the compiler can see is ours, not EF's,
 * and the generator can do it.
 *
 * What is left divides in three, and only the middle group can reach zero by editing:
 *   declared    — the JSON reflection fallback itself, the template accessor and Temporal's resolver
 *                 (both hard-fail under AOT rather than resolving reflectively), FromAssembly.
 *   needs the SG — AdapterFieldPolicy. (ConfigurationSectionResolver counts but is a false alarm:
 *                 GetCustomAttribute on a compile-time-known type raises no IL warning and is
 *                 AOT-safe; removing it would break consumers who have the package but not the
 *                 generator, silently reading the wrong config section.)
 *   EF model building — Persistence.EFCore's collection-element resolution over runtime expression
 *                 trees. EF is the stated exception, and this one is genuinely EF-shaped.
 *
 * Never raise it to make a build pass — the number falling is the whole point of it existing.
 */
const REFLECTION_BUDGET = 9;

// Reflection a template writes into the consumer application. Zero, and it started there:
// a template resolves types and members at compile time by definition.
const EMITTED_REFLECTION_BUDGET = 0;

/**
 * How many of the framework's public attributes an example application writes.
 *
 * ⚠️ This one goes UP, unlike every other number in this file. It is a floor, not a budget: an
 * example that stops writing an attribute is a reader who stops being shown it, and that happens
 * silently. `node scripts/example-coverage.mjs` names what each example writes and what none does.
 *
 * An adoption raises it only when the declaration has an effect a test observes: take the attribute
 * away and something goes red — an assertion, an integration test, or the compilation. An attribute
 * written where the default would do the same declares nothing, and stays green with it removed;
 * that is not an adoption, whatever the count says.
 *
 * Raise it when examples adopt more. Never lower it to make a build pass: the drop IS the finding.
 */
const EXAMPLE_COVERAGE_FLOOR = 260;

/**
 * Attributes nothing reads, and settable properties of read attributes that nothing names.
 *
 * **0** attributes: a public attribute the framework declares has a reader, from the commit that
 * declares it. At zero the ratchet holds a property rather than recording a debt.
 *
 * **0** properties, and that half has never fired on the real tree: its case is synthetic, plus the
 * control below. Say so rather than letting a zero read as coverage.
 *
 * ⚠️ The control, which is what says the wire is live — `[MessageMiddleware]`'s reader taken away, in
 * two halves, each measured on its own:
 *
 *   - take away the registration that reads the attribute, and the only appearance left is inside
 *     `RegisterShapeDiagnostic`: attributes go up by one;
 *   - take away only the named-argument read (`a.Key == "ForMessageType"`), and the type stays read
 *     while the member is named nowhere: properties go up by one.
 *
 * Both fail the gate, which is the point. ⚠️ And a property is only counted for a type that IS read:
 * an unread type subsumes its members rather than reporting them twice, so the two halves never both
 * fire for one defect.
 *
 * Down only. A budget lowered to make a build pass is the finding thrown away.
 */
const UNREAD_ATTRIBUTE_BUDGET = 0;

const UNREAD_PROPERTY_BUDGET = 0;

/**
 * AOT/trimming suppressions in runtime source.
 * A suppression is a declaration that the code is not AOT-safe. Lower it by fixing the code.
 */
const AOT_SUPPRESSION_BUDGET = 20;

/**
 * Where the gate puts bin/ and obj/, instead of alongside the sources.
 *
 * Sharing bin/ with whatever else is running — an IDE, a test host — breaks the build. The gate does
 * not delete anything (it builds with --no-incremental), but rewriting an output means removing the
 * previous one, and a file held open by another process fails the build with MSB3061, naming a
 * project nobody touched.
 *
 * Separate output directories remove the contention rather than policing it, and they mean the
 * machine stays usable while the gate runs — which, at ten minutes a run, is the difference between
 * a gate people run and one they postpone.
 */
const ARTIFACTS = ['--artifacts-path', 'artifacts/build'];

/**
 * Compares each suite's test count against the last run, and says so when one drops.
 *
 * "Did anything fail" is not the same question as "did everything run". A test host that dies partway
 * through reports the tests it managed to finish, none of them failed, and a gate asking only the
 * first goes green having executed fewer tests than exist, without a word.
 *
 * A partial drop is reported, not enforced: removing tests is legitimate, and a gate that fails on it
 * would be edited to shut up. What is not legitimate is a drop nobody noticed.
 *
 * A drop to ZERO is different, and is enforced. Nobody deletes every test in a suite and leaves the
 * project in the solution: it means the suite did not run. A generator defect that makes the Showcase
 * test host overflow its stack prints `Showcase.IntegrationTests: 511 -> 0`, and without enforcement
 * GREEN follows on the next line, because summarise asks "did anything fail" and a host that dies
 * reports neither a pass nor a failure.
 *
 * The zeroed suite's baseline is left untouched, so the next run fails too. Recording the 0 would make
 * this a one-shot warning: fail once, then green for ever against a baseline of nothing.
 */
function compareCounts(results) {
  let previous = {};
  try {
    previous = JSON.parse(readFileSync(COUNTS_FILE, 'utf8'));
  } catch {
    // First run, or the file was cleaned: nothing to compare against yet.
  }

  const current = {};
  const drops = [];
  const vanished = [];

  for (const r of results) {
    if (r.buildError) continue;
    const name = basename(r.proj, '.csproj');
    const total = r.passed + r.failed + r.skipped;
    const before = previous[name];

    // A suite that runs nothing after running something did not lose its tests: it did not run.
    // Its baseline is deliberately not updated — recording 0 would make the next run green.
    if (total === 0 && before) {
      vanished.push(`${name}: ${before} → 0`);
      continue;
    }

    if (r.noTests) continue;
    current[name] = total;

    if (before !== undefined && total < before)
      drops.push(`${name}: ${before} → ${total}`);
  }

  try {
    mkdirSync(dirname(COUNTS_FILE), { recursive: true });
    // Merged, not replaced: `--tier all` summarises twice, once per tier, and writing only the
    // current tier's suites would drop the other hundred — leaving the baseline covering whichever
    // tier happened to finish last.
    writeFileSync(COUNTS_FILE, `${JSON.stringify({ ...previous, ...current }, null, 2)}\n`);
  } catch {
    // Recording the baseline is a convenience; failing to do so must not fail the gate.
  }

  if (drops.length > 0) {
    console.log(`\n  ${bad('!')} fewer tests than the previous run — deliberate, or a host that died mid-suite?`);
    for (const drop of drops) console.log(`    ${drop}`);

    // ⚠️ Said out loud, because the two readings look identical in the output and only one of them
    // is worth debugging. A run that both loses suites AND runs fewer tests than last time is the
    // shape of lost hosts, not of broken code — seven suites red, 11216 tests against 11753, and
    // every one of them green on its own afterwards.
    if (results.some((r) => r.failed > 0 || r.buildError)) {
      console.log(`\n  ${bad('!')} suites are red AND fewer tests ran: that is the shape of a run that`);
      console.log('    lost test hosts, not of a code failure. Re-run before believing it.');
    }
  }

  if (vanished.length === 0) return true;

  console.log(`\n  ${bad('✗')} a suite ran no tests at all, and used to — it did not run:`);
  for (const gone of vanished) console.log(`    ${gone}`);
  return false;
}

// ── main ─────────────────────────────────────────────────────────────────────

/**
 * Writes what failed to a file, so a red run survives the terminal it was printed in.
 *
 * ⚠️ The names were never missing — they are printed above, with the message and the pool state.
 * What was missing is that they existed ONLY there: a scrollback that has moved on, a filtered pipe,
 * a CI log that was not kept. The mechanism lives in lib/failure-record.mjs, where it can be tested.
 */
function recordFailures(failed) {
  if (failed.length === 0) return;

  try {
    const path = recordFailureRun({
      dir: join('artifacts', 'gate'),
      tier,
      only: process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1] : null,
      suites: failed.map((r) => ({
        suite: basename(r.proj, '.csproj'),
        failed: r.failed,
        passed: r.passed,
        buildError: Boolean(r.buildError),
        tests: r.failures ?? [],
        // Beside `tests`, never instead of it: the terminal keeps its one capped line per test, and
        // the record keeps the runner's own account — the whole message and the frames with their
        // file and line. Six runs of a four-minute container suite bought eleven words before this
        // existed, and the cause was a stack frame away.
        details: failureDetails(r.out ?? ''),
      })),
    });

    console.log(`  ${dim(`↳ recorded in ${path}`)}`);
  } catch (err) {
    // A gate that cannot write its own record still has a verdict to deliver. Said, not swallowed.
    console.log(`  ${dim(`↳ could not record the failures: ${err.message}`)}`);
  }
}

function summarise(results) {
  const failed = results.filter((r) => r.failed > 0 || r.buildError);
  const passed = results.reduce((s, r) => s + r.passed, 0);
  const skipped = results.reduce((s, r) => s + r.skipped, 0);
  console.log(`\n  ${passed} passed, ${skipped} skipped, ${failed.length} suite(s) not green`);
  for (const r of failed) console.log(`    ${bad('✗')} ${basename(r.proj, '.csproj')}`);
  recordFailures(failed);
  const everySuiteRan = compareCounts(results);
  return failed.length === 0 && everySuiteRan;
}

// ⚠️ One run at a time. Everything builds into a single artifacts/build, so a second gate deletes
// what the first is compiling — and the wreck reads as a code failure: measured, a --tier
// docker started beside a --tier full reports "390 error markers". Refused with a reason
// instead. `verify` is exempt: it touches nothing and exists to be cheap.
const runLock = tier === 'verify' ? null : acquireRunLock('artifacts');
if (tier !== 'verify' && runLock === null) {
  console.error(bad('\nAnother gate run holds the lock') + dim(` (${describeHolder('artifacts')})`));
  console.error(dim('Two runs share artifacts/build and destroy each other. Wait for it, or kill it.'));
  process.exit(2);
}
process.on('exit', () => runLock?.release());

const all = findTestProjects();
const everyDocker = all.filter(requiresDocker);
const everyHermetic = all.filter((p) => !everyDocker.includes(p));

// --only narrows the suites and nothing else: every ratchet and the clean build still run, because
// those are the half a suite cannot see.
const matchesOnly = (proj) =>
  !only || basename(proj, '.csproj').toLowerCase().includes(String(only).toLowerCase());
const docker = everyDocker.filter(matchesOnly);
const hermetic = everyHermetic.filter(matchesOnly);

console.log(`\n${dim(`${all.length} test projects — ${hermetic.length} hermetic, ${docker.length} require Docker`)}\n`);

if (only) {
  if (hermetic.length + docker.length === 0) {
    console.error(bad(`--only '${only}' matches no suite. Nothing would run, which is not a pass.`));
    process.exit(2);
  }
  console.log(`  ${bad('!')} --only '${only}': ${hermetic.length + docker.length} of ${all.length} suites. `
    + `${dim('NOT the gate - every ratchet and the clean build still run, but the suites are a subset.')}`);
}

/**
 * Test projects this gate finds on disk but never builds, because they are not in the solution.
 *
 * The two halves of the gate disagreed: suites are DISCOVERED by walking the tree, and BUILT by
 * building the solution. A project outside it was run anyway — against whatever assembly happened to
 * be in its bin/ from an IDE build weeks earlier. Pragmatic.Authorization.Management.Tests spent an
 * unknown amount of time reporting 7 green tests from a binary nobody had rebuilt, which is worse
 * than not running them: it is a pass nobody earned.
 *
 * Only isolated output exposed it — a clean artifacts directory has no stale assembly to fall back on.
 */
// A test host outliving its run holds files in artifacts/build and fails the next clean build.
clearOrphanedTestHosts();

const orphans = all.filter((p) => !readFileSync(SLN, 'utf8').includes(basename(p)));
if (orphans.length > 0) {
  console.log(`  ${bad('!')} not in ${SLN} — discovered but never built by this gate:`);
  for (const p of orphans) console.log(`      ${p}`);
  console.log(`  ${dim('a suite here runs against whatever is left in its bin/, or not at all')}\n`);
}

let good = true;

if (tier === 'verify') {
  good = verifyPaths();
} else if (tier === 'fast') {
  console.log('fast — build + hermetic tests for changed modules');
  console.log(dim('  NOT the gate: incremental build. Use --tier all before committing.\n'));
  good = buildSolution({ clean: false });
  if (good) {
    const mods = changedModules();
    const subset = mods.size
      ? hermetic.filter((p) => [...mods].some((m) => p.replace(/\\/g, '/').startsWith(`${m}/`)))
      : [];
    if (!subset.length) console.log(dim('  no changed module with hermetic tests — nothing to run'));
    else good = summarise(await runSuites(subset, { parallel: true }));
  }
} else if (tier === 'full' || tier === 'all') {
  const fullStartedAt = performance.now();
  console.log('full — clean build --warnaserror + every hermetic suite');
  good = verifyPaths() && everyProjectIsInTheSolution() && everyTestSourceHasAProject() && skillCoverageRatchet() && silentDropsRatchet() && hostProvidedContractsRatchet() && buildSolution({ clean: true })
    && onlyPragmaticPackagesArePacked()
    && noReflectionJsonInGeneratedCode() && trimWarningRatchet() && undeclaredReflectionRatchet()
    && reflectionSourceRatchet()
    && exampleCoverageRatchet()
    && recordedReasonsNameRealProofs()
    && theCoverageMeasureHolds()
    && attributeReaderRatchet()
    && theReaderMeasureHolds()
    && emittedReflectionRatchet()
    && aotSuppressionRatchet()
    && siteDiagnosticsCheckCountsRows()
    && siteLinksResolveFromTheirSource()
    && nodeTestCases('scripts/sync-skill-examples.test.mjs', 'the skill-examples sync refuses every drift')
    && nodeTestCases('scripts/skill-portability.test.mjs', 'the skill-portability check refuses what another agent misreads')
    && nodeTestCases('scripts/commit-message-check.test.mjs', 'the commit-message check refuses what the public history must not carry')
    && nodeTestCases('scripts/run-samples.test.mjs', 'the sample runner fails a sample that does not exit 0')
    && contractsRatchetReadsWhatItClaims()
    && suiteRunnerOverlaps()
    && silentDropRuleHolds()
    && gateLeavesNoProcesses()
    && gateCleansOnlyItsOwn()
    && theFirstRunHolds()
    && generatedCodeNamesNoWallClock()
    && docsNameNoGenericEntity()
    && packageLinksAreChecked()
    && nativeBinariesAreCurrent()
    && failureRecordWorks()
    && vulnerabilityVerdictExpires()
    && oneRunAtATime()
    && diagnosticsInSync()
    && siteListsEveryDiagnostic();
  if (good) {
    // After the build, so the restore the scan needs has already happened. Its result is ANDed rather
    // than short-circuited: a known CVE and a failing test are independent signals, and letting the
    // first hide the second would cost a whole run to learn the second half.
    const supply = supplyChain({ ok, bad, dim });
    const suitesStartedAt = performance.now();
    const hermeticResults = await runSuites(hermetic, { parallel: true });
    console.log(dim(`  ${hermetic.length} hermetic suites in ${took(suitesStartedAt)}`));
    // ANDed like the supply chain: a broken sample and a failing test are independent signals.
    const samples = samplesRun();
    good = summarise(hermeticResults) && supply && samples;
  }

  console.log(dim(`  full: ${took(fullStartedAt)}`));

  if (good && tier === 'all') {
    const aotStartedAt = performance.now();
    console.log('\naot — publish each smoke Native AOT and run it');
    if (!nativeToolchainAvailable()) {
      console.log(bad('  The native toolchain is not reachable — these cannot run. NOT a pass.'));
      console.log(dim('  Windows: install a Visual Studio C++ workload.'));
      good = false;
    } else {
      good = await aotSmokes();
    }
    console.log(dim(`  aot: ${took(aotStartedAt)}`));
  }

  if (good && tier === 'all') {
    const dockerStartedAt = performance.now();
    console.log(`\ndocker — ${docker.length} container suites, ${dockerPace()}`);
    if (!dockerAvailable()) {
      console.log(bad('  Docker is not reachable — these suites cannot run. NOT a pass.'));
      good = false;
    } else {
      good = summarise(await runContainerSuites(docker));
    }
    console.log(dim(`  docker: ${took(dockerStartedAt)}`));
  }
} else if (tier === 'aot') {
  console.log('aot — publish each smoke Native AOT and run it');
  if (!nativeToolchainAvailable()) {
    console.log(bad('  The native toolchain is not reachable — these cannot run. NOT a pass.'));
    good = false;
  } else {
    good = await aotSmokes();
  }
} else if (tier === 'docker') {
  const dockerStartedAt = performance.now();
  console.log(`docker — ${docker.length} container suites, ${dockerPace()}`);
  if (!dockerAvailable()) {
    console.log(bad('  Docker is not reachable — these suites cannot run. NOT a pass.'));
    good = false;
  } else {
    good = buildSolution({ clean: false })
      && summarise(await runContainerSuites(docker));
  }
  console.log(dim(`  docker: ${took(dockerStartedAt)}`));
} else {
  console.error(`unknown --tier '${tier}' (fast | full | aot | docker | all | verify)`);
  process.exit(2);
}

runLock?.release();

console.log(good ? `\n${ok('GREEN')}\n` : `\n${bad('NOT GREEN')}\n`);
process.exit(good ? 0 : 1);
