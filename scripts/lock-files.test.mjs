/**
 * Every project CI restores has a NuGet lock file, and every CI restore is locked to it.
 *
 *   node --test scripts/lock-files.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ilLinkVersion, lockFileProblems, solutionProjects } from './lock-files.mjs';

const PROPS = `<Project>
  <PropertyGroup>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
  </PropertyGroup>
</Project>`;

const WHOLE = {
  projects: ['A/src/A/A.csproj', 'A/tests/A.Tests/A.Tests.csproj'],
  lockFiles: ['A/src/A/packages.lock.json', 'A/tests/A.Tests/packages.lock.json'],
  workflows: [{ path: '.github/workflows/ci.yml', text: '      - run: dotnet restore X.slnx --locked-mode\n' }],
  buildProps: PROPS,
  globalJson: '{ "sdk": { "version": "10.0.303", "rollForward": "patch" } }',
  ilLinkVersions: new Map([['10.0.11', 2]]),
};

test('every project locked, every restore locked: nothing to report', () => {
  assert.deepEqual(lockFileProblems(WHOLE), []);
});

test('a project without a lock file is reported: its restore is pinned by nothing', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, lockFiles: ['A/src/A/packages.lock.json'] }), [
    'A/tests/A.Tests/A.Tests.csproj has no packages.lock.json',
  ]);
});

test('a lock file left behind by a removed project is reported', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, lockFiles: [...WHOLE.lockFiles, 'B/src/B/packages.lock.json'] }), [
    'B/src/B/packages.lock.json belongs to no project in the solution',
  ]);
});

test('a workflow restore without --locked-mode is reported with its line', () => {
  const workflows = [{ path: '.github/workflows/ci.yml', text: 'jobs:\n      - run: dotnet restore X.slnx\n' }];
  assert.deepEqual(lockFileProblems({ ...WHOLE, workflows }), [
    '.github/workflows/ci.yml:2 restores without --locked-mode',
  ]);
});

test('a workflow line that only mentions restoring in prose is not a restore', () => {
  const workflows = [{ path: 'w.yml', text: '# the build step restores on its own\n      - run: dotnet build --no-restore\n' }];
  assert.deepEqual(lockFileProblems({ ...WHOLE, workflows }), []);
});

test('props without the lock file or without locked mode under CI are reported', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, buildProps: '<Project />' }), [
    'Directory.Build.props does not set RestorePackagesWithLockFile',
    "Directory.Build.props does not set RestoreLockedMode when '$(CI)' is true",
  ]);
});

test('locked mode everywhere, not only in CI, is not what is asked: a developer could never update a package', () => {
  const props = PROPS.replace(` Condition="'$(CI)' == 'true'"`, '');
  assert.deepEqual(lockFileProblems({ ...WHOLE, buildProps: props }), [
    "Directory.Build.props does not set RestoreLockedMode when '$(CI)' is true",
  ]);
});

test('an SDK that rolls forward to the latest patch is reported: the next patch moves every lock file', () => {
  const globalJson = '{ "sdk": { "version": "10.0.302", "rollForward": "latestPatch" } }';
  assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), [
    "global.json rolls forward to 'latestPatch': a newer SDK patch would move every lock file",
  ]);
});

test('no rollForward at all is reported: the pin has to be written, not left to the default', () => {
  const globalJson = '{ "sdk": { "version": "10.0.303" } }';
  assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), [
    "global.json names no rollForward: say 'patch', so the SDK that wrote the lock files is the one used",
  ]);
});

test('lock files that resolve two ILLink.Tasks versions are reported: two SDKs wrote them', () => {
  const ilLinkVersions = new Map([['10.0.11', 200], ['10.0.12', 1]]);
  assert.deepEqual(lockFileProblems({ ...WHOLE, ilLinkVersions }), [
    'the lock files resolve Microsoft.NET.ILLink.Tasks at more than one version (10.0.11 in 200, 10.0.12 in 1): written by different SDKs',
  ]);
});

test('the ILLink.Tasks version a lock file resolves, or null without it', () => {
  const lock = (deps) => JSON.stringify({ version: 2, dependencies: { 'net10.0': deps } });
  assert.equal(ilLinkVersion(lock({ 'Microsoft.NET.ILLink.Tasks': { type: 'Direct', resolved: '10.0.11' } })), '10.0.11');
  assert.equal(ilLinkVersion(lock({ MinVer: { type: 'Direct', resolved: '6.0.0' } })), null);
});

test('the projects of a .slnx, with forward slashes', () => {
  const slnx = '<Solution>\n  <Folder Name="/a/">\n    <Project Path="A/src/A/A.csproj" />\n    <Project Path="B\\src\\B\\B.csproj" />\n    <File Path="README.md" />\n  </Folder>\n</Solution>';
  assert.deepEqual(solutionProjects(slnx), ['A/src/A/A.csproj', 'B/src/B/B.csproj']);
});
