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

// Before a release the skills say what is not in the released packages yet. Moved by `set`, "not in alpha.1" would
// read "not in alpha.2" about the very release that ships it: a false statement, written by the release step.
for (const note of [
  'the engine (not in 1.0.0-alpha.1); otherwise',
  'Neither package is in 1.0.0-alpha.1: the engine ships later.',
  'None of these packages is in 1.0.0-alpha.1: later.',
  '<!-- New after alpha.1: on a local feed until the next release -->',
  // Wrapped where the prose wraps: the note starts on the line where "not in" is.
  'reference `Pragmatic.Audit.Engines` (not in\n1.0.0-alpha.1). The boundary\'s saves write the entries',
]) {
  test(`set refuses while a skill says something is not in the current version: ${note}`, () => {
    const withNote = files();
    withNote.set(SKILL, withNote.get(SKILL) + `\n${note}\n`);

    assert.throws(() => setVersion(withNote, '1.0.0-alpha.2', { tagExists: () => true }),
      (e) => e.message.includes(`${SKILL}:4`) && e.message.includes('1.0.0-alpha.1'));
  });
}

test('a note in the documentation the site publishes refuses set too, and the documentation is not rewritten', () => {
  const doc = 'Pragmatic.Testing/docs/worker-host.md';
  const documents = new Map([[doc, '# Worker host\n\nThe package is not in 1.0.0-alpha.1: it ships with the next release.\n']]);

  assert.throws(() => setVersion(files(), '1.0.0-alpha.2', { tagExists: () => true, documents }),
    (e) => e.message.includes(`${doc}:3`));

  const { changed } = setVersion(files(), '1.0.0-alpha.2', { tagExists: () => true, documents: new Map([[doc, 'nothing pending\n']]) });
  assert.equal(changed.has(doc), false);
});

test('the notes about an unreleased feature do not fail the check: before the release they are true', () => {
  const withNote = files();
  withNote.set(SKILL, withNote.get(SKILL) + '\nthe engine (not in 1.0.0-alpha.1); otherwise\n');

  assert.deepEqual(versionProblems(withNote), []);
});

test('set refuses to start from skills that already disagree', () => {
  assert.throws(() => setVersion(files({ other: '1.0.0-alpha.2' }), '1.0.0-alpha.3', { tagExists: () => true }), /more than one/);
});
