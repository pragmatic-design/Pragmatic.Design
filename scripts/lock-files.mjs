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
 * Regenerate after changing a package: `dotnet restore Pragmatic.Design.slnx --force-evaluate`.
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
 * }} input paths with forward slashes, relative to the repository root
 * @returns {string[]}
 */
export function lockFileProblems({ projects, lockFiles, workflows, buildProps }) {
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

  return problems;
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
