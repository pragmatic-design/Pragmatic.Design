/**
 * Every package ships a build for each target framework (#931).
 *
 *   node --test scripts/package-targets.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { targetProblems } from './package-targets.mjs';

test('a runtime package on both targets has no problem', () => {
  const packages = new Map([['Pragmatic.X.1.0.0.nupkg', ['lib/net10.0/Pragmatic.X.dll', 'lib/net11.0/Pragmatic.X.dll']]]);
  assert.deepEqual(targetProblems(packages), []);
});

test('a runtime package on net10.0 only is named, with the target it misses', () => {
  const packages = new Map([['Pragmatic.X.1.0.0.nupkg', ['lib/net10.0/Pragmatic.X.dll', 'README.md']]]);
  assert.deepEqual(targetProblems(packages), ['Pragmatic.X.1.0.0.nupkg: lib/ has net10.0, not net11.0']);
});

test('a .NET tool is held to the same targets under tools/', () => {
  const packages = new Map([['Pragmatic.Cli.1.0.0.nupkg', ['tools/net10.0/any/Pragmatic.Cli.dll']]]);
  assert.deepEqual(targetProblems(packages), ['Pragmatic.Cli.1.0.0.nupkg: tools/ has net10.0, not net11.0']);
});

test('a package whose only build is netstandard is not checked: it loads on any runtime', () => {
  const packages = new Map([['Pragmatic.X.CodeFixers.1.0.0.nupkg', ['lib/netstandard2.0/Pragmatic.X.CodeFixers.dll']]]);
  assert.deepEqual(targetProblems(packages), []);
});

// A generator or an analyzer runs in the compiler, whatever the application targets: it ships no lib/.
test('a package with neither lib/ nor tools/ is not checked', () => {
  const packages = new Map([['Pragmatic.Gen.1.0.0.nupkg', ['analyzers/dotnet/cs/Pragmatic.Gen.dll']]]);
  assert.deepEqual(targetProblems(packages), []);
});
