/**
 * NuGet lock files: every project CI restores carries one, and every restore in CI is locked to it.
 *
 * Central package management fixes the versions a project asks for, not what a restore brings back: a package
 * re-published under the same version, or a transitive version resolved differently, restores something else
 * with no change in the repository. A `packages.lock.json` records each package's content hash, and a locked
 * restore fails when what it would resolve differs from it.
 *
 * Two halves, both needed. `Directory.Build.props` turns the lock files on everywhere and turns locked mode on
 * when `CI` is true (GitHub Actions sets it), which reaches the restores that `dotnet build` and `dotnet test`
 * run on their own. The workflows' explicit `dotnet restore` lines say `--locked-mode` as well, so the step that
 * restores reads as locked where it is written.
 *
 * The SDK is part of what a lock file records. It adds `Microsoft.NET.ILLink.Tasks` to the trimmable projects at the
 * version of the runtime it bundles, so a newer SDK patch resolves a newer one and every locked restore fails. So
 * `global.json` pins the patch (`rollForward: patch`, the exact version when it is installed, which `setup-dotnet`
 * does), and every lock file has to agree on one ILLink version: two of them mean the files were written by two SDKs.
 *
 * Regenerate after changing a package, or the SDK in `global.json`:
 * `dotnet restore Pragmatic.Design.slnx --force-evaluate`.
 */

/**
 * What is missing: a project in the solution with no lock file beside it, a lock file whose project is not in
 * the solution, a `dotnet restore` in a workflow without `--locked-mode`, and either property absent from the
 * build props. Empty when everything CI restores is locked.
 *
 * @param {{
 *   projects: string[],
 *   lockFiles: string[],
 *   workflows: { path: string, text: string }[],
 *   buildProps: string,
 *   globalJson: string,
 *   ilLinkVersions: Map<string, number>,
 * }} input paths with forward slashes, relative to the repository root; `ilLinkVersions` counts the lock files
 *   per ILLink.Tasks version they resolve
 * @returns {string[]}
 */
export function lockFileProblems({ projects, lockFiles, workflows, buildProps, globalJson, ilLinkVersions }) {
  const problems = [];
  const dir = (path) => path.slice(0, path.lastIndexOf('/'));
  const projectDirs = new Set(projects.map(dir));
  const lockDirs = new Set(lockFiles.map(dir));

  for (const project of projects)
    if (!lockDirs.has(dir(project))) problems.push(`${project} has no packages.lock.json`);

  for (const lockFile of lockFiles)
    if (!projectDirs.has(dir(lockFile))) problems.push(`${lockFile} belongs to no project in the solution`);

  for (const { path, text } of workflows) {
    text.split('\n').forEach((line, i) => {
      if (/\bdotnet\s+restore\b/.test(line) && !line.includes('--locked-mode'))
        problems.push(`${path}:${i + 1} restores without --locked-mode`);
    });
  }

  if (!/<RestorePackagesWithLockFile>\s*true\s*<\/RestorePackagesWithLockFile>/.test(buildProps))
    problems.push('Directory.Build.props does not set RestorePackagesWithLockFile');
  if (!/<RestoreLockedMode\s+Condition="'\$\(CI\)'\s*==\s*'true'">\s*true\s*<\/RestoreLockedMode>/.test(buildProps))
    problems.push("Directory.Build.props does not set RestoreLockedMode when '$(CI)' is true");

  // Required in so many words: an absent policy leaves the reader to know the SDK's default.
  const rollForward = JSON.parse(globalJson).sdk?.rollForward;
  if (rollForward === undefined)
    problems.push("global.json names no rollForward: say 'patch', so the SDK that wrote the lock files is the one used");
  else if (rollForward !== 'patch' && rollForward !== 'disable')
    problems.push(`global.json rolls forward to '${rollForward}': a newer SDK patch would move every lock file`);

  if (ilLinkVersions.size > 1) {
    const counts = [...ilLinkVersions].map(([version, n]) => `${version} in ${n}`).join(', ');
    problems.push(`the lock files resolve Microsoft.NET.ILLink.Tasks at more than one version (${counts}): written by different SDKs`);
  }

  return problems;
}

/**
 * The version of `Microsoft.NET.ILLink.Tasks` a lock file resolves, or null when the project does not reference it.
 *
 * @param {string} lockFile the file's text
 * @returns {string | null}
 */
export function ilLinkVersion(lockFile) {
  const targets = JSON.parse(lockFile).dependencies ?? {};
  for (const packages of Object.values(targets)) {
    const resolved = packages['Microsoft.NET.ILLink.Tasks']?.resolved;
    if (resolved) return resolved;
  }
  return null;
}

/**
 * The projects a `.slnx` lists, as written in it with forward slashes.
 *
 * @param {string} slnx
 * @returns {string[]}
 */
export function solutionProjects(slnx) {
  return [...slnx.matchAll(/<Project\s+Path="([^"]+\.csproj)"/g)].map((m) => m[1].replaceAll('\\', '/'));
}
