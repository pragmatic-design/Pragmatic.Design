/**
 * The gate cleans up what is its own, and nothing else.
 *
 * ⚠️ Not by **kind**: `docker container prune -f` with no filter and `taskkill /IM testhost.exe /F`.
 * On the machine of somebody who just cloned the repository — which is where `AGENTS.md` sends them
 * to run it — that means another project's stopped containers, their unused volumes, and an IDE's
 * running test session. On a contributor's machine `docker ps -a` can hold an unrelated compose stack
 * and **bagetter**, the local NuGet feed this repository's own how-to tells you to run.
 *
 *   node --test scripts/gate-ownership.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  TESTCONTAINERS_LABEL, ownLeftovers, ownTestHosts, ownedByThisRepo, parseProcessLine, processListing,
} from './lib/gate-ownership.mjs';

const REPO = 'C:\\Pragmatic\\Pragmatic.Design';

const windowsListing = [
  `1111\ttesthost.exe\t"C:\\Program Files\\dotnet\\testhost.exe" --port 5000 ${REPO}\\artifacts\\build\\Some.Tests.dll`,
  '2222\ttesthost.exe\t"C:\\Program Files\\dotnet\\testhost.exe" --port 5001 C:\\Other\\Project\\bin\\Other.Tests.dll',
  '3333\tdevenv.exe\t"C:\\Program Files\\Microsoft Visual Studio\\devenv.exe"',
].join('\n');

test('a test host running this repository is ours', () => {
  const mine = ownTestHosts(windowsListing, { windows: true, repoRoot: REPO });

  assert.deepEqual(mine.map((p) => p.pid), [1111],
    'only the host whose command line names this checkout may be killed');
});

test("another solution's test host is left alone", () => {
  const mine = ownTestHosts(windowsListing, { windows: true, repoRoot: REPO });

  assert.ok(!mine.some((p) => p.pid === 2222),
    'killing it is how the gate interrupted a test run somebody else started');
});

test('processes that are not test hosts are never considered', () => {
  const mine = ownTestHosts(windowsListing, { windows: true, repoRoot: REPO });

  assert.ok(!mine.some((p) => p.name === 'devenv'),
    'the filter is by name first — an editor is not a test host however its command line reads');
});

test('a POSIX listing is read too', () => {
  const listing = [
    `4444 testhost /usr/share/dotnet/testhost --port 5000 ${REPO}/artifacts/build/Some.Tests.dll`,
    '5555 testhost /usr/share/dotnet/testhost --port 5001 /home/other/bin/Other.Tests.dll',
  ].join('\n');

  const mine = ownTestHosts(listing, { windows: false, repoRoot: REPO });

  assert.deepEqual(mine.map((p) => p.pid), [4444]);
});

/**
 * A suite can start processes of its own — the Agent tests start the daemon as the executable it is
 * in a deployment — and a test host killed before it disposes them leaves them running out of the
 * gate's output, which fails the next clean build with MSB3061 (#965). Their image is `dotnet`, not
 * a test host, so ownership is read from where they run: the gate's isolated output.
 */
const daemonListing = [
  `6666\tdotnet.exe\t"C:\\Program Files\\dotnet\\dotnet.exe" exec --runtimeconfig ${REPO}\\artifacts\\build\\bin\\Pragmatic.Agent.Tests\\debug_net10.0\\Pragmatic.Agent.Tests.runtimeconfig.json ${REPO}\\artifacts\\build\\bin\\Pragmatic.Agent.Tests\\debug_net10.0\\Pragmatic.Agent.dll start --port 0`,
  `7777\tdotnet.exe\t"C:\\Program Files\\dotnet\\dotnet.exe" build ${REPO}\\Pragmatic.Design.slnx`,
  `8888\tdotnet.exe\t"C:\\Program Files\\dotnet\\dotnet.exe" exec ${REPO}\\Pragmatic.Agent\\src\\Pragmatic.Agent\\bin\\Debug\\net10.0\\Pragmatic.Agent.dll start`,
  '9999\tdotnet.exe\t"C:\\Program Files\\dotnet\\dotnet.exe" "C:\\Program Files\\dotnet\\sdk\\11.0.100\\MSBuild.dll" /nologo /nodemode:1 /nodeReuse:false',
  `1111\ttesthost.exe\t"C:\\Program Files\\dotnet\\testhost.exe" --port 5000 ${REPO}\\artifacts\\build\\Some.Tests.dll`,
].join('\n');

test('a process a suite started, running out of the gate output, is a leftover of ours', () => {
  const mine = ownLeftovers(daemonListing, { windows: true, repoRoot: REPO });

  assert.ok(mine.some((p) => p.pid === 6666),
    'the Agent daemon a killed test host left behind holds its DLLs and fails the next clean build');
  assert.ok(mine.some((p) => p.pid === 1111), 'and the test hosts are still ours');
});

test('a dotnet process of this repository that does not run out of the gate output is left alone', () => {
  const mine = ownLeftovers(daemonListing, { windows: true, repoRoot: REPO });

  assert.ok(!mine.some((p) => p.pid === 7777), 'a build somebody started on this checkout');
  assert.ok(!mine.some((p) => p.pid === 8888), 'an Agent run from the IDE output, bin/ and not artifacts/build');
  assert.ok(!mine.some((p) => p.pid === 9999), 'an MSBuild node: reusable build infrastructure');
});

test('a POSIX leftover is read too', () => {
  const listing = `4444 dotnet /usr/share/dotnet/dotnet exec ${REPO}/artifacts/build/bin/Pragmatic.Agent.Tests/debug_net10.0/Pragmatic.Agent.dll start`;

  assert.deepEqual(ownLeftovers(listing, { windows: false, repoRoot: REPO }).map((p) => p.pid), [4444]);
});

/**
 * ⚠️ Run, not read: the query once went through `cmd.exe`, which cut the PowerShell pipeline at its
 * `|`, and every listing on Windows came back empty with exit 255 — a cleanup that cleared nothing,
 * with nothing saying so (#965). The one process sure to be there is this one.
 */
test('the process listing finds a process that is running: this one', () => {
  const listing = processListing('^node');

  assert.equal(listing.code, 0, `the listing failed: ${listing.out}`);
  const rows = listing.out.split('\n').map((line) => parseProcessLine(line, { windows: process.platform === 'win32', names: ['node'] }));
  assert.ok(rows.some((p) => p?.pid === process.pid), `pid ${process.pid} is not in the listing`);
});

test('the separator leaning the other way still matches', () => {
  assert.ok(ownedByThisRepo(`dotnet exec ${REPO.replace(/\\/g, '/')}/artifacts/x.dll`, REPO),
    'MSBuild writes forward slashes on Windows and a person writes backslashes; both are this repo');
});

test('an empty command line owns nothing', () => {
  assert.equal(ownedByThisRepo('', REPO), false);
  assert.equal(ownedByThisRepo('dotnet test', ''), false);
});

/**
 * ⚠️ The control for the Docker half, and the reason it is a source assertion rather than a run:
 * pruning for real would delete something, which is the exact thing under test. What can regress
 * silently is the filter being dropped, so that is what is held.
 */
test('both prunes carry the ownership label', () => {
  const source = readFileSync('scripts/check.mjs', 'utf-8')
    .replace(/\/\*[\s\S]*?\*\//g, (m) => ' '.repeat(m.length))
    .replace(/(^|\n)([^\n]*?)\/\/[^\n]*/g, (m, head, before) =>
      head + before + ' '.repeat(m.length - head.length - before.length));

  const prunes = source.match(/run\('docker', \[[^\]]*'prune'[^\]]*\]\)/g) ?? [];

  assert.equal(prunes.length, 2, 'containers and volumes');
  for (const prune of prunes) {
    assert.match(prune, /'--filter'/, `an unfiltered prune deletes other people's work: ${prune}`);
    assert.match(prune, /TESTCONTAINERS_LABEL/, `and it must be the ownership label: ${prune}`);
  }
});

test('the label is the one Testcontainers actually writes', () => {
  assert.equal(TESTCONTAINERS_LABEL, 'org.testcontainers.version',
    'read out of Testcontainers.dll 3.10 — there is no bare org.testcontainers=true, and a filter '
    + 'that matches nothing prunes nothing without saying so');
});
