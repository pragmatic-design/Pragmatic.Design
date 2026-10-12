/**
 * The skills name one package version, the two plugin manifests agree, and `set` moves both (#218).
 *
 *   node --test scripts/skills-version.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { MANIFESTS, setVersion, versionProblems } from './skills-version.mjs';

const SKILL = 'marketplace/plugins/pragmatic-design/skills/pragmatic-use-x/SKILL.md';
const OTHER = 'marketplace/plugins/pragmatic-design/skills/pragmatic-ecosystem/references/packages.md';

function files({ skill = '1.0.0-alpha.1', other = '1.0.0-alpha.1', plugin = '0.11.31', marketplace = '0.11.31' } = {}) {
  return new Map([
    [SKILL, `<PackageReference Include="Pragmatic.Result" Version="${skill}" />\r\nname the version (\`${skill}\`).\r\n`],
    [OTHER, `<PragmaticVersion>${other}</PragmaticVersion>\n<PackageReference Include="Pragmatic.Mapping" Version="${other}" />\n`],
    [MANIFESTS.plugin, `{\n  "name": "pragmatic-design",\n  "version": "${plugin}",\n  "description": "x"\n}\n`],
    [MANIFESTS.marketplace, `{\n  "plugins": [\n    {\n      "name": "pragmatic-design",\n      "version": "${marketplace}"\n    }\n  ]\n}\n`],
  ]);
}

test('skills that name one version, and manifests that agree, have no problem', () => {
  assert.deepEqual(versionProblems(files()), []);
});

test('two package versions in the skills are refused, naming the files of each', () => {
  const problems = versionProblems(files({ other: '1.0.0-alpha.2' }));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /1\.0\.0-alpha\.1.*pragmatic-use-x/s);
  assert.match(problems[0], /1\.0\.0-alpha\.2.*packages\.md/s);
});

test('manifests that disagree on the plugin version are refused', () => {
  const problems = versionProblems(files({ marketplace: '0.11.30' }));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /0\.11\.31.*0\.11\.30/s);
});

test('set rewrites every occurrence, prose included, and raises both manifests', () => {
  const { changed, from, plugin } = setVersion(files(), '1.0.0-alpha.2', { tagExists: () => true });

  assert.equal(from, '1.0.0-alpha.1');
  assert.deepEqual(plugin, { from: '0.11.31', to: '0.11.32' });
  assert.equal(changed.get(SKILL), '<PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.2" />\r\nname the version (`1.0.0-alpha.2`).\r\n');
  assert.match(changed.get(OTHER), /<PragmaticVersion>1\.0\.0-alpha\.2<\/PragmaticVersion>/);
  assert.match(changed.get(MANIFESTS.plugin), /"version": "0\.11\.32"/);
  assert.match(changed.get(MANIFESTS.marketplace), /"version": "0\.11\.32"/);
  assert.deepEqual(versionProblems(new Map([...files(), ...changed])), []);
});

// The scaffold sets <PragmaticVersion> once and references it: `$(PragmaticVersion)` is the property, not
// a second version.
test('a reference to the version property is not a version', () => {
  const withProperty = files();
  withProperty.set('marketplace/plugins/pragmatic-design/skills/pragmatic-ecosystem/references/scaffold/solution-template.md',
    '<PackageReference Include="Pragmatic.Composition" Version="$(PragmaticVersion)" />\n');

  assert.deepEqual(versionProblems(withProperty), []);
});

test('set refuses a version that was never released', () => {
  assert.throws(() => setVersion(files(), '1.0.0-alpha.9', { tagExists: () => false }), /nuget-v1\.0\.0-alpha\.9/);
});

test('set refuses to start from skills that already disagree', () => {
  assert.throws(() => setVersion(files({ other: '1.0.0-alpha.2' }), '1.0.0-alpha.3', { tagExists: () => true }), /more than one/);
});
