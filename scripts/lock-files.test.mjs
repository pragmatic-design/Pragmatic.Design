/**
 * Every project CI restores has a NuGet lock file, and every CI restore is locked to it.
 *
 *   node --test scripts/lock-files.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bundledILLinkVersion, ilLinkVersion, lockFileProblems, pinnedILLinkVersion, solutionProjects } from './lock-files.mjs';

const PROPS = `<Project>
  <PropertyGroup>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
  </PropertyGroup>
</Project>`;

const pin = (version) => `<Project>
  <Target Name="PinILLinkPack" BeforeTargets="ProcessFrameworkReferences">
    <ItemGroup>
      <KnownILLinkPack Condition="'%(Identity)' == 'Microsoft.NET.ILLink.Tasks' AND '%(TargetFramework)' == 'net10.0'">
        <ILLinkPackVersion>${version}</ILLinkPackVersion>
      </KnownILLinkPack>
    </ItemGroup>
  </Target>
</Project>`;

const WHOLE = {
  projects: ['A/src/A/A.csproj', 'A/tests/A.Tests/A.Tests.csproj'],
  lockFiles: ['A/src/A/packages.lock.json', 'A/tests/A.Tests/packages.lock.json'],
  workflows: [{ path: '.github/workflows/ci.yml', text: '      - run: dotnet restore X.slnx --locked-mode\n' }],
  buildProps: PROPS,
  buildTargets: pin('10.0.11'),
  globalJson: '{ "sdk": { "version": "10.0.303", "rollForward": "feature" } }',
  bundledILLink: '10.0.11',
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

test('a clone builds with a newer SDK of another feature band: rolling forward by feature is accepted', () => {
  const globalJson = '{ "sdk": { "version": "10.0.303", "rollForward": "feature" } }';
  assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), []);
});

test("'patch' stays accepted: stricter than 'feature', and still installed exactly by setup-dotnet", () => {
  const globalJson = '{ "sdk": { "version": "10.0.303", "rollForward": "patch" } }';
  assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), []);
});

for (const rollForward of ['latestPatch', 'latestFeature', 'latestMinor', 'latestMajor']) {
  test(`'${rollForward}' is reported: setup-dotnet would install whatever SDK is newest, and CI's lock files move with it`, () => {
    const globalJson = `{ "sdk": { "version": "10.0.303", "rollForward": "${rollForward}" } }`;
    assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), [
      `global.json rolls forward to '${rollForward}': setup-dotnet would install the newest SDK instead of 10.0.303, and every locked restore in CI would fail`,
    ]);
  });
}

for (const rollForward of ['minor', 'major']) {
  test(`'${rollForward}' is reported: a local build may leave the 10.0 line`, () => {
    const globalJson = `{ "sdk": { "version": "10.0.303", "rollForward": "${rollForward}" } }`;
    assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), [
      `global.json rolls forward to '${rollForward}': a build could run on an SDK outside 10.0, which no lock file was written by`,
    ]);
  });
}

test('no rollForward at all is reported: the pin has to be written, not left to the default', () => {
  const globalJson = '{ "sdk": { "version": "10.0.303" } }';
  assert.deepEqual(lockFileProblems({ ...WHOLE, globalJson }), [
    "global.json names no rollForward: say 'feature', so CI installs the SDK that wrote the lock files and a newer local one still builds",
  ]);
});

test('lock files that resolve two ILLink.Tasks versions are reported: two SDKs wrote them', () => {
  const ilLinkVersions = new Map([['10.0.11', 200], ['10.0.12', 1]]);
  assert.deepEqual(lockFileProblems({ ...WHOLE, ilLinkVersions }), [
    'the lock files resolve Microsoft.NET.ILLink.Tasks at more than one version (10.0.11 in 200, 10.0.12 in 1): written by different SDKs',
  ]);
});

// SDK 10.0.303 adds ILLink.Tasks 10.0.11 and 10.0.402 adds 10.0.13: with the version left to the SDK, an IDE on
// the newer one rewrote the lock files in the background (#301).
test('the trimmer left to the SDK is reported: the lock files would record whichever SDK restored last', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, buildTargets: '<Project />' }), [
    'Directory.Build.targets does not pin Microsoft.NET.ILLink.Tasks: the lock files would record whichever SDK restored last',
  ]);
});

test('a pin the SDK in global.json does not bundle is reported: the SDK moved and the trimmer stayed behind', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, bundledILLink: '10.0.13' }), [
    'Directory.Build.targets pins Microsoft.NET.ILLink.Tasks 10.0.11, but SDK 10.0.303 in global.json bundles 10.0.13: move the pin with the SDK',
  ]);
});

test('without that SDK installed the pin is not compared with it: only CI is sure to have it', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, bundledILLink: null }), []);
});

test('lock files that resolve a version other than the pin are reported: they were written before it moved', () => {
  assert.deepEqual(lockFileProblems({ ...WHOLE, ilLinkVersions: new Map([['10.0.13', 2]]) }), [
    'the lock files resolve Microsoft.NET.ILLink.Tasks 10.0.13, but Directory.Build.targets pins 10.0.11: regenerate them',
  ]);
});

test('the pinned version is read from the targets, and the bundled one from the SDK for net10.0 only', () => {
  assert.equal(pinnedILLinkVersion(pin('10.0.11')), '10.0.11');
  assert.equal(pinnedILLinkVersion('<Project />'), null);

  const bundled = `<Project>
    <KnownILLinkPack Include="Microsoft.NET.ILLink.Tasks"
                     TargetFramework="net10.0"
                     ILLinkPackVersion="10.0.11" />
    <KnownILLinkPack Include="Microsoft.NET.ILLink.Tasks"
                     TargetFramework="net9.0"
                     ILLinkPackVersion="9.0.19" />
  </Project>`;
  assert.equal(bundledILLinkVersion(bundled), '10.0.11');
  assert.equal(bundledILLinkVersion('<Project />'), null);
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
