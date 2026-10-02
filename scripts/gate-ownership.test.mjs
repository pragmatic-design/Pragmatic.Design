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
import { TESTCONTAINERS_LABEL, ownTestHosts, ownedByThisRepo } from './lib/gate-ownership.mjs';

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
