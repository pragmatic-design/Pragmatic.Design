/**
 * What on this machine belongs to this checkout — and therefore what the gate may delete.
 *
 * ⚠️ Not by **kind**: `docker container prune -f` with no filter, and `taskkill /IM testhost.exe /F`.
 * Both are correct on a dedicated CI box and wrong on the machine of the person who just cloned the
 * repository — which is the machine `AGENTS.md` sends them to run it on, where `docker ps -a` can
 * hold an unrelated project's compose stack and **bagetter**, the local NuGet feed this repository's
 * own how-to tells you to run.
 *
 * Ownership is read from two things that are actually available: the label Testcontainers writes on
 * what it creates, and the repository path on a test host's command line.
 */
import { spawnSync } from 'node:child_process';

/**
 * The label Testcontainers puts on every container and volume it creates.
 *
 * ⚠️ Read out of `Testcontainers.dll` 3.10, not assumed: what it writes is
 * `org.testcontainers.version`, `.lang`, `.session-id` and `.resource-reaper-session`. There is no
 * bare `org.testcontainers=true` in this version, which is the label a guess would have reached for —
 * and a filter that matches nothing prunes nothing, silently.
 */
export const TESTCONTAINERS_LABEL = 'org.testcontainers.version';

/**
 * Whether a process command line belongs to the checkout rooted at `repoRoot`.
 *
 * Compared case-insensitively and on both slash spellings: a command line assembled by MSBuild and
 * one typed by a person disagree about which way a separator leans, and on Windows they disagree
 * about the drive letter's case too.
 */
export function ownedByThisRepo(commandLine, repoRoot) {
  if (!commandLine || !repoRoot) return false;

  const slashes = (text) => text.toLowerCase().replace(/\\/g, '/');

  return slashes(commandLine).includes(slashes(repoRoot));
}

/**
 * One row of a process listing → `{pid, name, commandLine}`, or null when it is not a test host.
 *
 * Windows rows are tab-separated (the CIM query below builds them that way); POSIX rows come from
 * `ps -eo pid=,comm=,args=` and are separated by runs of spaces, so only the first two fields can be
 * split on — everything after them is the command line, spaces and all.
 */
export function parseProcessLine(line, { windows, names }) {
  const trimmed = (line ?? '').trim();
  if (trimmed.length === 0) return null;

  const parts = windows ? trimmed.split('\t') : trimmed.split(/\s+/);
  const pid = Number.parseInt(parts[0], 10);
  if (!Number.isInteger(pid)) return null;

  const name = (parts[1] ?? '').replace(/\.exe$/i, '');
  if (!names.includes(name)) return null;

  return { pid, name, commandLine: parts.slice(2).join(' ') };
}

/**
 * The processes whose image name matches `pattern`, one row each: tab-separated `pid, name, command
 * line` on Windows, `ps -eo pid=,comm=,args=` elsewhere (`parseProcessLine` reads both).
 *
 * ⚠️ Spawned without a shell. Through `cmd.exe` — what check.mjs's `run` does on Windows — the
 * PowerShell pipeline is cut at its first `|`, the query exits 255 and returns nothing, and a caller
 * that reads "nothing" as "nothing left over" kills nothing: the gate's cleanup never cleared a single
 * test host on Windows that way, which is how a daemon left running could fail two clean builds (#965).
 *
 * @returns {{ code: number, out: string }}
 */
export function processListing(pattern) {
  const r = process.platform === 'win32'
    ? spawnSync('powershell', ['-NoProfile', '-Command',
        `Get-CimInstance Win32_Process | Where-Object { $_.Name -match '${pattern}' } `
        + '| ForEach-Object { "$($_.ProcessId)`t$($_.Name)`t$($_.CommandLine)" }'], { encoding: 'utf8' })
    : spawnSync('ps', ['-eo', 'pid=,comm=,args='], { encoding: 'utf8' });

  return { code: r.status ?? -1, out: `${r.stdout ?? ''}` };
}

/**
 * Where the gate builds to, from the repository's root: the isolated output (`--artifacts-path` in
 * check.mjs) that nothing but the gate writes or runs from.
 */
export const GATE_OUTPUT = 'artifacts/build/';

/**
 * Every process in `listing` left over from an earlier run that this checkout may kill: its test
 * hosts, and the `dotnet` processes running out of the gate's output.
 *
 * ⚠️ The second kind is what a suite starts itself — the Agent tests run the daemon as the executable
 * it is in a deployment — and outlives a test host killed before it disposed of it, holding its DLLs
 * so that the next clean build fails with MSB3061 on a project nobody touched (#965). Its image is
 * `dotnet`, so the name does not tell it from a build somebody runs on this checkout; where it runs
 * from does. Not the repository path: a `dotnet build` of the solution and an IDE's run out of `bin/`
 * name it too, and neither is the gate's to stop.
 */
export function ownLeftovers(listing, { windows, repoRoot }) {
  const output = `${repoRoot.replace(/[\\/]+$/, '')}/${GATE_OUTPUT}`;
  const hosts = ownTestHosts(listing, { windows, repoRoot });
  const started = (listing ?? '')
    .split('\n')
    .map((line) => parseProcessLine(line, { windows, names: ['dotnet'] }))
    .filter((p) => p !== null && ownedByThisRepo(p.commandLine, output));

  return [...hosts, ...started];
}

/**
 * The test hosts in `listing` that this checkout may kill.
 */
export function ownTestHosts(listing, { windows, repoRoot, names = ['testhost', 'vstest.console'] }) {
  return (listing ?? '')
    .split('\n')
    .map((line) => parseProcessLine(line, { windows, names }))
    .filter((p) => p !== null && ownedByThisRepo(p.commandLine, repoRoot));
}
