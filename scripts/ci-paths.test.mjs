/**
 * Which changes may skip the build in CI (#199): Markdown that nothing compiles, packs or syncs, and the site. Packed
 * Markdown builds and packs, and runs no suite.
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

test('any file that is not Markdown, outside the site, runs everything', () => {
  for (const path of ['scripts/check.mjs', '.github/workflows/ci.yml', '.github/workflows/docs.yml', 'Pragmatic.Design.slnx', 'Directory.Build.props', 'benchmarks/allocation-baseline.json'])
    assert.equal(scopeOf([path]), 'full', path);
});

// No .NET project reads the site. docs.yml builds it on every pull request, and the docs tier checks the pages synced into
// it (#814 and #817 ran the whole build and the container suites, about 25 minutes, for an astro bump and a home page).
test('anything under the site runs the docs tier', () => {
  for (const path of ['site/logo.png', 'site/web/src/pages/index.astro', 'site/package.json', 'site/pnpm-lock.yaml', 'site/scripts/sync-docs.mjs'])
    assert.equal(scopeOf([path]), 'docs', path);
});

test('Markdown that is packed into a package runs the pack scope: build and pack, no suites', () => {
  // The root README is the fallback package README; a module's README and its docs/ ship in its packages. Pack & Validate
  // reads them, which needs the build; no test reads them.
  for (const path of ['README.md', 'Pragmatic.Identity/README.md', 'Pragmatic.Identity/docs/packages.md', 'Pragmatic.Logging/docs/sub/deep.md'])
    assert.equal(scopeOf([path]), 'pack', path);
});

test('the widest path decides: packed Markdown with the site packs, and one C# file with either runs everything', () => {
  assert.equal(scopeOf(['README.md', 'site/web/src/pages/index.astro', 'docs/ROADMAP.md']), 'pack');
  assert.equal(scopeOf(['README.md', 'Pragmatic.Identity/src/Pragmatic.Identity/ICurrentUser.cs']), 'full');
  assert.equal(scopeOf(['site/package.json', 'global.json']), 'full');
});

test('a skill runs everything: its examples are synced from compiled sources', () => {
  assert.equal(scopeOf(['marketplace/plugins/pragmatic-design/skills/pragmatic-use-identity/SKILL.md']), 'full');
});

test('no change at all runs everything, rather than nothing', () => {
  assert.equal(scopeOf([]), 'full');
});

test('a path written with backslashes is read like one with slashes', () => {
  assert.equal(scopeOf(['Pragmatic.Identity\\docs\\packages.md']), 'pack');
  assert.equal(scopeOf(['site\\web\\src\\pages\\index.astro']), 'docs');
  assert.equal(scopeOf(['docs\\howto\\showcase-walkthrough.md']), 'docs');
});
