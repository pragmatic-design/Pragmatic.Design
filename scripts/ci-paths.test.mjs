/**
 * Which changes may skip the build in CI (#199): only Markdown that nothing compiles, packs or syncs.
 *
 *   node --test scripts/ci-paths.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scopeOf } from './ci-paths.mjs';

test('a committed benchmark report runs the docs tier', () => {
  // #191: one report, and 31 minutes of CI.
  assert.equal(scopeOf(['Pragmatic.Endpoints/benchmarks/reports/scope-resolution-136_ScopeResolutionBenchmarks-report-github.md']), 'docs');
});

test('the repository docs, the guides and the site copies run the docs tier', () => {
  assert.equal(scopeOf([
    'docs/howto/showcase-walkthrough.md',
    'docs/security/untrusted-input.md',
    'site/docs/src/content/docs/guides/showcase-walkthrough.md',
    'examples/showcase/README.md',
    'CONTRIBUTING.md',
  ]), 'docs');
});

test('one C# file among the Markdown runs everything', () => {
  assert.equal(scopeOf(['docs/howto/showcase-walkthrough.md', 'Pragmatic.Identity/src/Pragmatic.Identity.Local/Services/BcryptPasswordHasher.cs']), 'full');
});

test('any file that is not Markdown runs everything', () => {
  for (const path of ['scripts/check.mjs', '.github/workflows/ci.yml', 'Pragmatic.Design.slnx', 'Directory.Build.props', 'benchmarks/allocation-baseline.json', 'site/logo.png'])
    assert.equal(scopeOf([path]), 'full', path);
});

test('Markdown that is packed into a package runs everything, since Pack & Validate reads it', () => {
  // The root README is the fallback package README; a module's README and its docs/ ship in its packages.
  for (const path of ['README.md', 'Pragmatic.Identity/README.md', 'Pragmatic.Identity/docs/packages.md', 'Pragmatic.Logging/docs/sub/deep.md'])
    assert.equal(scopeOf([path]), 'full', path);
});

test('a skill runs everything: its examples are synced from compiled sources', () => {
  assert.equal(scopeOf(['marketplace/plugins/pragmatic-design/skills/pragmatic-use-identity/SKILL.md']), 'full');
});

test('no change at all runs everything, rather than nothing', () => {
  assert.equal(scopeOf([]), 'full');
});

test('a path written with backslashes is read like one with slashes', () => {
  assert.equal(scopeOf(['Pragmatic.Identity\\docs\\packages.md']), 'full');
  assert.equal(scopeOf(['docs\\howto\\showcase-walkthrough.md']), 'docs');
});
