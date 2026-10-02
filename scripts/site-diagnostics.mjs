/**
 * site-diagnostics.mjs — every declared diagnostic has a row on the published reference page.
 *
 * `docs/diagnostics.md` is generated from the descriptors themselves (`sync-diagnostics.mjs`), so it
 * cannot fall behind the code. The page a user actually reads,
 * `site/docs/src/content/docs/reference/diagnostics.md`, is written by hand: it says what the
 * number means and what to do about it, which no descriptor holds. A reference that answers "no such
 * diagnostic" for a diagnostic the build just emitted is worse than no reference: it sends the reader
 * looking for a typo in their own code.
 *
 * This is one-way on purpose. A row the dictionary has no descriptor for is legitimate — the
 * suppressors (`PRAGS###`) have rows and are not `DiagnosticDescriptor`s — so only the missing
 * direction fails.
 *
 * Usage: node scripts/site-diagnostics.mjs [--check]
 *   --check  same verdict, and the exit code is what the gate reads.
 */

import { readFileSync, existsSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// PRAGMATIC_SITE_DIAGNOSTICS_ROOT lets the test drive the script over a throwaway tree, exactly as
// PRAGMATIC_DIAGNOSTICS_ROOT does for sync-diagnostics.mjs. Unset in every real run.
const ROOT = process.env.PRAGMATIC_SITE_DIAGNOSTICS_ROOT
  ? resolve(process.env.PRAGMATIC_SITE_DIAGNOSTICS_ROOT)
  : join(dirname(fileURLToPath(import.meta.url)), "..");

const DICTIONARY = join(ROOT, "docs", "diagnostics.md");
const PAGE = join(ROOT, "site", "docs", "src", "content", "docs", "reference", "diagnostics.md");

/** The rows of the dictionary's per-range tables: `| ID | Symbol | Severity | Title |`. */
export function declaredDiagnostics(dictionary) {
  const declared = new Map();
  for (const line of dictionary.split("\n")) {
    const row = /^\| `(PRAG[0-9A-Za-z]+)`[^|]*\| `\w+` \| (\w+) \| ([^|]*)\|/.exec(line);
    if (row && !declared.has(row[1])) declared.set(row[1], { severity: row[2], title: row[3].trim() });
  }
  return declared;
}

/**
 * The IDs the page documents.
 *
 * A row, not a mention: the page names IDs in its prose and in its range table too, and counting
 * those would let a diagnostic pass as documented because some other row happens to cite it.
 */
export function documentedDiagnostics(page) {
  const documented = new Set();
  for (const line of page.split("\n")) {
    const row = /^\| `(PRAG[0-9A-Za-z]+)` \|/.exec(line);
    if (row) documented.add(row[1]);
  }
  return documented;
}

// A missing input fails: with no dictionary nothing is declared, and the check would pass by
// comparing nothing with the page.
for (const [what, path] of [["the dictionary", DICTIONARY], ["the reference page", PAGE]]) {
  if (!existsSync(path)) {
    console.error(`FAIL ${what} does not exist: ${path}`);
    process.exit(1);
  }
}
const dictionary = readFileSync(DICTIONARY, "utf8");
const page = readFileSync(PAGE, "utf8");

const declared = declaredDiagnostics(dictionary);
if (declared.size === 0) {
  console.error(`FAIL no diagnostic read from ${DICTIONARY}: its table format has changed`);
  process.exit(1);
}
const documented = documentedDiagnostics(page);
const missing = [...declared.keys()].filter((id) => !documented.has(id)).sort();

if (missing.length) {
  console.error(
    `FAIL ${missing.length} of ${declared.size} declared diagnostics have no row on ` +
    "site/docs/src/content/docs/reference/diagnostics.md");
  for (const id of missing) console.error(`  ${id} — ${declared.get(id).severity} — ${declared.get(id).title}`);
  console.error("  Fix: add a row per ID (meaning and fix), in the section of its range.");
  process.exit(1);
}

console.log(`ok ${declared.size} declared diagnostics, all with a row on the reference page`);
