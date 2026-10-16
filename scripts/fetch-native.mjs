#!/usr/bin/env node
/**
 * fetch-native.mjs — puts in each module's runtimes/ the native libraries CI built from the current source.
 *
 * The libraries are not committed (#159). pdf-native.yml and imaging-native.yml build them and upload each
 * one under a name that carries its source hash (native-stamp.mjs `artifactName`), so any run, of any
 * workflow, finds the build of the source it checked out. A library already in place with the right stamp
 * is kept; one fetched is checked for that stamp before it is placed.
 *
 *   node scripts/fetch-native.mjs                     this machine's RID, every crate
 *   node scripts/fetch-native.mjs --rid all           every RID (packing)
 *   node scripts/fetch-native.mjs --rid linux-x64     one RID; repeatable
 *   node scripts/fetch-native.mjs --root base         another checkout (the benchmarks' base)
 *   node scripts/fetch-native.mjs --run-id <id>       only artifacts that run uploaded (a release builds its own)
 *   node scripts/fetch-native.mjs --probe             fetch nothing: say which crates have no build of this
 *                                                     source, as `<crate>=missing|present` lines, also
 *                                                     written to $GITHUB_OUTPUT when it is set
 *
 * Needs the GitHub CLI, authenticated (GH_TOKEN in a workflow, with `actions: read`). Without a build of
 * this source it fails and says how to make one: the crate's workflow, or node scripts/refresh-native.mjs.
 */
import { spawnSync } from 'node:child_process';
import { appendFileSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { CRATES, HOST_RID, ROOT, artifactName, sourceHash, stampMismatch } from './native-stamp.mjs';

const args = process.argv.slice(2);
const option = (name) => {
  const values = [];
  args.forEach((a, i) => { if (a === name && args[i + 1]) values.push(args[i + 1]); });
  return values;
};

const probe = args.includes('--probe');
const root = resolve(option('--root')[0] ?? ROOT);
const runId = option('--run-id')[0];
const requested = option('--rid');
const ALL = [...new Set(CRATES.flatMap((c) => c.targets.map((t) => t.rid)))];
const rids = requested.includes('all') ? ALL : requested.length ? requested : [HOST_RID];
const unknown = rids.filter((r) => !ALL.includes(r));
if (unknown.length || !rids.length) {
  console.error(`unknown RID: ${unknown.join(', ') || '(none for this platform)'} — known: ${ALL.join(', ')}`);
  process.exit(2);
}

function gh(ghArgs) {
  const r = spawnSync('gh', ghArgs, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (r.error || r.status !== 0)
    throw new Error(`gh ${ghArgs.join(' ')} failed: ${r.error?.message ?? (r.stderr || '').trim()}`);
  return r.stdout;
}

const repo = process.env.GITHUB_REPOSITORY || gh(['repo', 'view', '--json', 'nameWithOwner', '-q', '.nameWithOwner']).trim();

/** The newest unexpired artifact of that name, from the given run when one is named. */
function findArtifact(name) {
  const listed = JSON.parse(gh(['api', `repos/${repo}/actions/artifacts?name=${encodeURIComponent(name)}&per_page=100`]));
  return listed.artifacts
    .filter((a) => !a.expired && (!runId || String(a.workflow_run?.id) === String(runId)))
    .sort((a, b) => b.created_at.localeCompare(a.created_at))[0] ?? null;
}

const hasCurrent = (path, c, hash) => existsSync(path) && stampMismatch(readFileSync(path), c.env, hash) === null;

const outcome = [];
let failed = false;
for (const c of CRATES) {
  const hash = sourceHash(root, c.sources);
  let missing = false;
  for (const t of c.targets.filter((x) => rids.includes(x.rid))) {
    const target = join(root, t.path);
    // A release takes only what its own run built, so a library already on disk does not count there.
    if (!runId && hasCurrent(target, c, hash)) {
      if (!probe) console.log(`   present  ${t.path}`);
      continue;
    }

    const name = artifactName(c, t.rid, hash);
    const artifact = findArtifact(name);
    if (!artifact) {
      missing = true;
      if (!probe) {
        failed = true;
        console.error(`   no build  ${name}${runId ? ` in run ${runId}` : ''}`);
      }
      continue;
    }
    if (probe) continue;

    const into = mkdtempSync(join(tmpdir(), `${name}-`));
    gh(['run', 'download', String(artifact.workflow_run.id), '--repo', repo, '-n', name, '-D', into]);
    const [file] = readdirSync(into);
    const problem = file ? stampMismatch(readFileSync(join(into, file)), c.env, hash) : 'is empty';
    if (problem) {
      failed = true;
      console.error(`   refused  ${name}: ${problem}`);
      continue;
    }
    mkdirSync(dirname(target), { recursive: true });
    copyFileSync(join(into, file), target);
    console.log(`   fetched  ${t.path} (run ${artifact.workflow_run.id})`);
  }
  outcome.push(`${c.name}=${missing ? 'missing' : 'present'}`);
}

if (probe) {
  console.log(outcome.join('\n'));
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `${outcome.join('\n')}\n`);
  process.exit(0);
}

if (failed) {
  console.error('\nNo CI build of this source for the libraries above. Either:');
  console.error('   - run the crate\'s workflow on this commit (pdf-native.yml, imaging-native.yml; CI runs it itself');
  console.error('     when a change has no build), then fetch again; or');
  console.error('   - build them here: node scripts/refresh-native.mjs  (a Rust toolchain; --linux needs Docker)');
  process.exit(1);
}
