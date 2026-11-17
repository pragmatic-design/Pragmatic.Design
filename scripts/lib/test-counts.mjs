/**
 * A count `dotnet test` prints, summed over every summary in the output.
 *
 * ⚠️ One summary per test assembly, and a test project that targets two frameworks is two assemblies:
 * `dotnet test` on it prints a summary for net10.0 and another for net11.0 (#832). Reading the first
 * match counted one target and dropped the other, so a red net11.0 after a green net10.0 reported green.
 *
 * @param {string} out The output of `dotnet test`.
 * @param {string} labels The label alternatives, e.g. 'Superati|Passed'.
 * @param {boolean} anchored True for the detailed logger's block, one count per line at its start; false for
 *     the default logger's single line, where the count sits mid-line.
 * @returns {number} The sum of every printed count for those labels.
 */
export function summedCount(out, labels, anchored) {
  const pattern = anchored
    ? new RegExp(`^\\s*(?:${labels}):\\s+(\\d+)`, 'gm')
    : new RegExp(`(?:${labels}):\\s+(\\d+)`, 'g');
  let total = 0;
  for (const match of out.matchAll(pattern)) total += Number(match[1]);
  return total;
}
