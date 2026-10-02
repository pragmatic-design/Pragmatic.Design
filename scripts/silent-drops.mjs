#!/usr/bin/env node
/**
 * silent-drops.mjs — transforms that throw away a declaration without saying so.
 *
 *   node scripts/silent-drops.mjs [--all] [--lines]
 *
 * `--all` lists every file instead of the worst twenty; `--lines` prints `file:line` per drop, which
 * is what triaging a file with nine of them needs — the name alone does not say where they are.
 *
 * A transform reached through `ForAttributeWithMetadataName` is looking at a node the user
 * DECORATED: the attribute is there, so the node is ours by construction. When such a transform
 * answers `return null`, it is saying one of two very different things —
 *
 *   "not the shape I handle"   (defensive, unreachable in practice)
 *   "yours, and malformed"     (the user wrote something we will not honour)
 *
 * — and the caller cannot tell them apart, because `null` looks the same. The second one ships as
 * silence: the attribute compiles, the generator produces nothing, and the build is green. Such a drop
 * is found by accident rather than by looking: a composite action exposed with no permission answering
 * 204 to a caller who holds none, a query property that generates no filter and answers 200 with the
 * wrong row, `[GenerateHierarchy]` on an entity whose foreign key is not called ParentId,
 * `[Resource("x")]` with no capabilities.
 *
 * WHAT IS COUNTED: `return null;` sites inside attribute-triggered transforms that never construct a
 * Diagnostic — the drops that cannot be explained to anyone, because the file they live in has no way
 * to say anything. The file count is reported alongside as context; the budget is on the sites, because
 * that is the number that moves when a transform is fixed. Giving one transform a reporting channel
 * takes all of its sites out at once.
 *
 * WHAT IT IS NOT: a bug count. Calibrated on AlternateKeyTransform, three of its six drops are real
 * rejections (a format on a non-string property, an empty format, a format with no placeholder) and
 * three are defensive guards that cannot happen. Roughly half, on that sample. The number here is the
 * population to triage, and the ratchet's job is to stop it growing while it is triaged.
 *
 * BUDGET is a freeze, not a target. It falls when a transform gains a way to report; it is never
 * raised to make a build pass.
 *
 * ⚠️ WHICH FILES: the ones on disk, not the ones git tracks. Asking `git ls-files` would make a
 * transform that has not been committed yet invisible, and committing it would then move the count
 * without a line of transform code changing. A number that depends on what has been committed
 * measures the commit, not the code.
 */
import { execSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { pathToFileURL } from "node:url";

/**
 * The frozen count. `node scripts/silent-drops.mjs` prints the current one, `--lines` every site.
 *
 * ⚠️ When it comes down, say whether the code changed or the **measure** did: a transform that gains
 * a way to report leaves the count, and so does a file the counter was misreading (see
 * RETURNS_A_DIAGNOSTIC_CARRIER). The two are different news in the same number, and must not be
 * confused.
 */
export const BUDGET = 209;

/** Every `.cs` under a directory, as paths relative to the repository root. */
function sourceFilesUnder(dir, root) {
  const found = [];

  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) found.push(...sourceFilesUnder(full, root));
    else if (entry.name.endsWith(".cs")) found.push(relative(root, full).split("\\").join("/"));
  }

  return found;
}

/**
 * A transform that answers with a **value-equatable diagnostic carrier** has a channel, even though it
 * never constructs a `Diagnostic`.
 *
 * ⚠️ It cannot construct one: a `Diagnostic` is not value-equatable, so putting one through an
 * incremental pipeline breaks caching. The generator's pattern is to return a carrier — today
 * `MessagingDiagnosticInfo(Kind, LocationInfo?, EquatableArray<string>)` — and to turn it into a
 * descriptor at the output edge. Without this rule, the pattern that exists **because** diagnostics
 * must cache correctly would read as silence, and the counter would count the one file whose whole job
 * is to report.
 *
 * Matched on the **return type of a transform**, not on the file mentioning a carrier: it is one token
 * on the signature and does not depend on how the body reads. The shape is the name's ending, not one
 * type's name, so a second carrier needs no edit here.
 */
const RETURNS_A_DIAGNOSTIC_CARRIER = /\b\w*DiagnosticInfo\??\s+\w+\s*\(\s*(?:this\s+)?GeneratorAttributeSyntaxContext/;

/**
 * One file's answer, from its text alone.
 *
 * Pure so it can be exercised on a few lines instead of on the repository — the rule is what decides
 * the number, and a rule only the whole tree can run is a rule nobody can check. See
 * `silent-drops.test.mjs`.
 *
 * @param {string} text the file's contents
 * @returns {{ isTransform: boolean, hasChannel: boolean, lines: number[] }}
 */
export function classify(text) {
  // Comment lines dropped first: a `return null;` quoted in a /// block, or the word Diagnostic in
  // a remark explaining why one is NOT reported, would each flip the answer for the wrong reason.
  // ⚠️ Blanked rather than removed, so a line number still means the line it is.
  const code = text.split("\n").map((l) => (/^\s*(\/\/|\/\*|\*)/.test(l) ? "" : l));
  const src = code.join("\n");

  return {
    isTransform: src.includes("GeneratorAttributeSyntaxContext"),
    hasChannel: /\bDiagnostic\b/.test(src) || RETURNS_A_DIAGNOSTIC_CARRIER.test(src),
    lines: code.map((l, i) => (l.includes("return null;") ? i + 1 : 0)).filter((n) => n > 0),
  };
}

/**
 * @returns {{ total: number, silent: {file: string, drops: number, lines: number[]}[], drops: number }}
 */
export function silentDrops() {
  const root = process.cwd();

  const features = join(root, "Pragmatic.SourceGenerator", "src", "Pragmatic.SourceGenerator", "Features");
  const files = existsSync(features) ? sourceFilesUnder(features, root) : [];

  let total = 0;
  const silent = [];

  for (const f of files) {
    const { isTransform, hasChannel, lines } = classify(readFileSync(join(root, f), "utf-8"));

    if (!isTransform) continue;
    total++;

    if (lines.length === 0 || hasChannel) continue;

    silent.push({ file: f, drops: lines.length, lines });
  }

  return { total, silent, drops: silent.reduce((s, x) => s + x.drops, 0) };
}

// --- CLI ---------------------------------------------------------------------------------------

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { total, silent, drops } = silentDrops();
  const all = process.argv.includes("--all");
  // Triaging a file with nine drops means finding them, and the name alone does not say where.
  const withLines = process.argv.includes("--lines");

  console.log(`transforms triggered by an attribute: ${total}`);
  console.log(`silent drop sites: ${drops} · budget ${BUDGET}`);
  console.log(`in ${silent.length} files with no channel to report a refusal
`);

  const listed = silent.sort((a, b) => b.drops - a.drops).slice(0, all || withLines ? silent.length : 20);

  for (const s of listed) {
    if (!withLines) {
      console.log(`  ${String(s.drops).padStart(2)}  ${s.file.split("/").pop()}`);
      continue;
    }

    // The full path, so the line is clickable from the repository root.
    for (const line of s.lines) console.log(`  ${s.file}:${line}`);
  }

  if (!all && !withLines && silent.length > 20)
    console.log(`  … and ${silent.length - 20} more (--all, or --lines for file:line)`);
}
