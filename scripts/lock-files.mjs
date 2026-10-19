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
 * The SDK was part of what a lock file recorded. It adds `Microsoft.NET.ILLink.Tasks` to the trimmable projects at
 * the version of the runtime it bundles: SDK 10.0.303 writes 10.0.11, 10.0.402 writes 10.0.13. Any restore by a newer
 * SDK rewrote every lock file, and an IDE that runs one in the background did it with nobody asking (#301). So
 * `Directory.Build.targets` pins that version, and the lock files no longer depend on the SDK that restores them.
 * The pin moves with the SDK in `global.json`. When that exact SDK is installed, as it always is in CI, the check
 * compares the two, and every lock file has to resolve the pinned version.
 *
 * `setup-dotnet` installs the exact SDK version unless `rollForward` starts with `latest`, which it turns into "the
 * newest 10.0" and friends. `rollForward: feature` keeps CI exact and still lets a clone build with any newer 10.0
 * SDK; `patch` used to be required, and stopped every contributor whose SDK was of a newer feature band.
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
 *   buildTargets: string,
 *   globalJson: string,
 *   bundledILLink: string | null,
 *   ilLinkVersions: Map<string, number>,
 * }} input paths with forward slashes, relative to the repository root; `bundledILLink` is the ILLink.Tasks
 *   version the SDK named in `global.json` bundles, null when that SDK is not installed; `ilLinkVersions` counts the
 *   lock files per ILLink.Tasks version they resolve
 * @returns {string[]}
 */
export function lockFileProblems({
  projects, lockFiles, workflows, buildProps, buildTargets, globalJson, bundledILLink, ilLinkVersions,
}) {
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
  const sdk = JSON.parse(globalJson).sdk;
  const rollForward = sdk?.rollForward;
  if (rollForward === undefined)
    problems.push("global.json names no rollForward: say 'feature', so CI installs the SDK that wrote the lock files and a newer local one still builds");
  else if (rollForward.startsWith('latest'))
    problems.push(`global.json rolls forward to '${rollForward}': setup-dotnet would install the newest SDK instead of ${sdk.version}, and every locked restore in CI would fail`);
  else if (rollForward === 'minor' || rollForward === 'major')
    problems.push(`global.json rolls forward to '${rollForward}': a build could run on an SDK outside ${sdk.version.split('.').slice(0, 2).join('.')}, which no lock file was written by`);

  if (ilLinkVersions.size > 1) {
    const counts = [...ilLinkVersions].map(([version, n]) => `${version} in ${n}`).join(', ');
    problems.push(`the lock files resolve Microsoft.NET.ILLink.Tasks at more than one version (${counts}): written by different SDKs`);
  }

  const pinned = pinnedILLinkVersion(buildTargets);
  if (pinned === null) {
    problems.push('Directory.Build.targets does not pin Microsoft.NET.ILLink.Tasks: the lock files would record whichever SDK restored last');
    return problems;
  }

  if (bundledILLink !== null && bundledILLink !== pinned)
    problems.push(`Directory.Build.targets pins Microsoft.NET.ILLink.Tasks ${pinned}, but SDK ${sdk.version} in global.json bundles ${bundledILLink}: move the pin with the SDK`);

  if (ilLinkVersions.size === 1) {
    const [resolved] = ilLinkVersions.keys();
    if (resolved !== pinned)
      problems.push(`the lock files resolve Microsoft.NET.ILLink.Tasks ${resolved}, but Directory.Build.targets pins ${pinned}: regenerate them`);
  }

  return problems;
}

/**
 * The version `Directory.Build.targets` pins for `Microsoft.NET.ILLink.Tasks` on net10.0, or null when it pins none.
 *
 * @param {string} buildTargets the file's text
 * @returns {string | null}
 */
export function pinnedILLinkVersion(buildTargets) {
  const match = buildTargets.match(
    /<KnownILLinkPack\s+Condition="[^"]*Microsoft\.NET\.ILLink\.Tasks[^"]*net10\.0[^"]*">\s*<ILLinkPackVersion>([^<]+)<\/ILLinkPackVersion>/,
  );
  return match ? match[1].trim() : null;
}

/**
 * The `Microsoft.NET.ILLink.Tasks` version an SDK bundles for net10.0, from its
 * `Microsoft.NETCoreSdk.BundledVersions.props`, or null when the file names none.
 *
 * @param {string} bundledVersionsProps the file's text
 * @returns {string | null}
 */
export function bundledILLinkVersion(bundledVersionsProps) {
  for (const [item] of bundledVersionsProps.matchAll(/<KnownILLinkPack\b[^>]*\/>/g)) {
    if (!/TargetFramework="net10\.0"/.test(item)) continue;
    const version = item.match(/ILLinkPackVersion="([^"]+)"/);
    if (version) return version[1];
  }
  return null;
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
