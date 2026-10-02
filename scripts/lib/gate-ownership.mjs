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

  const here = repoRoot.toLowerCase();
  const haystack = commandLine.toLowerCase();

  return haystack.includes(here) || haystack.includes(here.replace(/\\/g, '/'));
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
 * The test hosts in `listing` that this checkout may kill.
 */
export function ownTestHosts(listing, { windows, repoRoot, names = ['testhost', 'vstest.console'] }) {
  return (listing ?? '')
    .split('\n')
    .map((line) => parseProcessLine(line, { windows, names }))
    .filter((p) => p !== null && ownedByThisRepo(p.commandLine, repoRoot));
}
