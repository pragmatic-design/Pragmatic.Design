/**
 * The distinct warnings of a build log, per project, for the ratchets that count them.
 *
 * MSBuild prints each warning twice — at the project and again in the summary — and a project that
 * targets two frameworks prints the same warning once per target, the line ending in
 * `[X.csproj::TargetFramework=net11.0]` instead of `[X.csproj]`. Halving the count handled the first and
 * doubled the second (#832): Pragmatic.Endpoints.OpenApi's four warnings counted eight the day it also
 * targeted net11.0, with no new warning in its source. A warning is one place in the source with one
 * code, so it is counted once, whichever target and whichever copy of the log printed it.
 */

/**
 * @param {string} output The build's console output.
 * @param {RegExp} warning The warnings to count, matched against each line (e.g. /warning IL(2026|3050)/).
 * @returns {Map<string, number>} Distinct warnings per project name (the .csproj without its extension).
 */
export function distinctWarningsPerProject(output, warning) {
  const seen = new Set();
  const perProject = new Map();

  for (const raw of output.split('\n')) {
    if (!warning.test(raw)) continue;

    const line = raw.trim().replace(/::TargetFramework=[^\]]*\]/, ']');
    const project = line.match(/([A-Za-z0-9_.]+)\.csproj/)?.[1];
    if (!project || seen.has(line)) continue;

    seen.add(line);
    perProject.set(project, (perProject.get(project) ?? 0) + 1);
  }

  return perProject;
}
