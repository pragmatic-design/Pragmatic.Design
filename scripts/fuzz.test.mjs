/**
 * The fuzz runner's own logic: which harnesses exist, and what libFuzzer is asked to do (#307). It runs no fuzzer:
 * that the harnesses run is proven by the nightly workflow, and that their kept inputs pass by the module's tests.
 *
 *   node --test scripts/fuzz.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DRIVER, SHARPFUZZ_VERSION, findTargets, libFuzzerArgs } from './fuzz.mjs';

test('every target of every fuzz.json under a module fuzz folder is found, with what it instruments', () => {
  const root = mkdtempSync(join(tmpdir(), 'fuzz-targets-'));
  const project = join(root, 'Pragmatic.A', 'fuzz', 'Pragmatic.A.Fuzz');
  mkdirSync(project, { recursive: true });
  writeFileSync(join(project, 'fuzz.json'), JSON.stringify({
    targets: { 'b-thing': { instrument: ['Pragmatic.A.dll'] }, 'a-thing': { instrument: ['Pragmatic.A.dll', 'X.dll'] } },
  }));
  mkdirSync(join(root, 'Pragmatic.B', 'fuzz', 'NoManifest'), { recursive: true });

  assert.deepEqual(findTargets(root), [
    { name: 'a-thing', project: 'Pragmatic.A/fuzz/Pragmatic.A.Fuzz', instrument: ['Pragmatic.A.dll', 'X.dll'] },
    { name: 'b-thing', project: 'Pragmatic.A/fuzz/Pragmatic.A.Fuzz', instrument: ['Pragmatic.A.dll'] },
  ]);
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
