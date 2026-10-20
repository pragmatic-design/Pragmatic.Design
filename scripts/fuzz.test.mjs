/**
 * The fuzz runner's own logic: which harnesses exist, and what libFuzzer is asked to do (#307). It runs no fuzzer:
 * that the harnesses run is proven by the nightly workflow, and that their kept inputs pass by the module's tests.
 *
 *   node --test scripts/fuzz.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { DRIVER, SHARPFUZZ_VERSION, findTargets, libFuzzerArgs } from './fuzz.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));

/**
 * The nightly fuzzes one harness per job (#158): run in one job, 15 harnesses of 600 s were 150 minutes against a
 * 120-minute timeout, and the run was cancelled before it ended, so its corpus cache was never saved. The matrix
 * reads the targets from the script, so a new harness joins the nightly without touching the workflow.
 */
test('--list-json prints every target name, the list the nightly matrix fuzzes', () => {
  const run = spawnSync(process.execPath, [join(HERE, 'fuzz.mjs'), '--list-json'], { encoding: 'utf8' });

  assert.equal(run.status, 0, run.stderr);
  assert.deepEqual(JSON.parse(run.stdout), findTargets().map((t) => t.name));
});

test('the nightly fuzzes each target in its own job, with its own corpus cache, and one failure stops no other', () => {
  const workflow = readFileSync(join(HERE, '..', '.github', 'workflows', 'fuzz.yml'), 'utf8');

  assert.match(workflow, /node scripts\/fuzz\.mjs --list-json/, 'the matrix is read from the script');
  assert.match(workflow, /target: \$\{\{ fromJSON\(needs\.targets\.outputs\.targets\) \}\}/);
  assert.match(workflow, /fail-fast: false/, 'a crash in one harness does not cancel the others');
  assert.match(workflow, /node scripts\/fuzz\.mjs \$\{\{ matrix\.target \}\}/, 'each job fuzzes its one target');
  assert.match(workflow, /path: artifacts\/fuzz\/\$\{\{ matrix\.target \}\}\/corpus/);
  assert.match(workflow, /key: fuzz-corpus-\$\{\{ matrix\.target \}\}-\$\{\{ github\.run_id \}\}/);
  assert.doesNotMatch(workflow, /fuzz\.mjs --all/, 'no job fuzzes every harness in sequence');
});

test('every target of every fuzz.json under a module fuzz folder is found, with what it instruments', () => {
  const root = mkdtempSync(join(tmpdir(), 'fuzz-targets-'));
  const project = join(root, 'Pragmatic.A', 'fuzz', 'Pragmatic.A.Fuzz');
  mkdirSync(project, { recursive: true });
  writeFileSync(join(project, 'fuzz.json'), JSON.stringify({
    targets: { 'b-thing': { instrument: ['Pragmatic.A.dll'] }, 'a-thing': { instrument: ['Pragmatic.A.dll', 'X.dll'] } },
  }));
  mkdirSync(join(root, 'Pragmatic.B', 'fuzz', 'NoManifest'), { recursive: true });

  assert.deepEqual(findTargets(root), [
    { name: 'a-thing', project: 'Pragmatic.A/fuzz/Pragmatic.A.Fuzz', instrument: ['Pragmatic.A.dll', 'X.dll'], dict: null },
    { name: 'b-thing', project: 'Pragmatic.A/fuzz/Pragmatic.A.Fuzz', instrument: ['Pragmatic.A.dll'], dict: null },
  ]);
});

test('a target that names a dictionary is found with it, relative to the root (#321)', () => {
  const root = mkdtempSync(join(tmpdir(), 'fuzz-dict-'));
  const project = join(root, 'Pragmatic.A', 'fuzz', 'Pragmatic.A.Fuzz');
  mkdirSync(join(project, 'Dictionaries'), { recursive: true });
  writeFileSync(join(project, 'Dictionaries', 'json.dict'), '"null"\n');
  writeFileSync(join(project, 'fuzz.json'), JSON.stringify({
    targets: { json: { instrument: ['A.dll'], dict: 'Dictionaries/json.dict' } },
  }));

  assert.equal(findTargets(root)[0].dict, 'Pragmatic.A/fuzz/Pragmatic.A.Fuzz/Dictionaries/json.dict');
});

// A dictionary named and absent would make libFuzzer refuse to start, in the nightly, hours after the change; a
// misspelt one found here is a red gate on the pull request.
test('a target that names a dictionary that is not there is refused', () => {
  const root = mkdtempSync(join(tmpdir(), 'fuzz-dict-missing-'));
  const project = join(root, 'Pragmatic.A', 'fuzz', 'Pragmatic.A.Fuzz');
  mkdirSync(project, { recursive: true });
  writeFileSync(join(project, 'fuzz.json'), JSON.stringify({
    targets: { json: { instrument: ['A.dll'], dict: 'Dictionaries/json.dict' } },
  }));

  assert.throws(() => findTargets(root), /json.*Dictionaries\/json\.dict/);
});

test('every JSON harness of the repository has a dictionary that exists (#321)', () => {
  const json = ['devexpress-adapter', 'grid-adapter-generated', 'gossip-datagram', 'i18n-json', 'message-type-registry',
    'optional-converter', 'primeng-adapter', 'result-converter', 'sql-headers', 'temporal-json'];
  const targets = findTargets();

  for (const name of json)
    assert.ok(targets.find((t) => t.name === name)?.dict, `${name} has no dictionary`);
});

test('libFuzzer is given the dictionary when the target has one, and nothing when it has none', () => {
  const base = { assembly: '/b/A.dll', seconds: 60, corpus: '/w/corpus', seeds: '/s/Corpus/x', crashes: '/w/crashes' };

  assert.ok(libFuzzerArgs({ ...base, dict: '/s/Dictionaries/x.dict' }).includes('-dict=/s/Dictionaries/x.dict'));
  assert.ok(!libFuzzerArgs(base).some((a) => a.startsWith('-dict')));
  assert.equal(libFuzzerArgs({ ...base, dict: '/s/Dictionaries/x.dict' }).at(-1), '/s/Corpus/x');
});

test('the repository has the frame codec harness', () => {
  assert.ok(findTargets().some((t) => t.name === 'frame-codec' && t.instrument.includes('Pragmatic.Agent.Protocol.dll')));
});

test('libFuzzer writes what it finds into the grown corpus, never among the seeds, and stops on time', () => {
  const args = libFuzzerArgs({ assembly: '/b/A.dll', seconds: 60, corpus: '/w/corpus', seeds: '/s/Corpus/x', crashes: '/w/crashes' });

  assert.equal(args.at(-2), '/w/corpus');
  assert.equal(args.at(-1), '/s/Corpus/x');
  assert.ok(args.includes('--target_path=dotnet'));
  assert.ok(args.includes('--target_arg=/b/A.dll'));
  assert.ok(args.includes('-max_total_time=60'));
  assert.ok(args.includes('-artifact_prefix=/w/crashes/'));
});

test('the driver is pinned to a commit and a hash, and the instrumenter to the package version', () => {
  assert.match(DRIVER.url, /\/libfuzzer-dotnet\/[0-9a-f]{40}\/libfuzzer-dotnet\.cc$/);
  assert.match(DRIVER.sha256, /^[0-9a-f]{64}$/);

  const props = readFileSync(new URL('../Directory.Packages.props', import.meta.url), 'utf8');
  assert.match(props, new RegExp(`Include="SharpFuzz" Version="${SHARPFUZZ_VERSION.replaceAll('.', '\\.')}"`));
});
