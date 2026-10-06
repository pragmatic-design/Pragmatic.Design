/**
 * sync-diagnostics.mjs — regenerates `docs/diagnostics.md` by scanning the sources.
 *
 * Every `DiagnosticDescriptor` declared in the repository, with its ID, symbol, severity and
 * location. The document is regenerated, never edited: a hand-edited copy becomes a second truth
 * that drifts from the first.
 *
 * It also finds what no other check finds: two descriptors with the same ID. Roslyn treats them as
 * one diagnostic, so suppressing one suppresses the other, and which severity wins cannot be decided
 * by reading either declaration alone.
 *
 * Usage: node scripts/sync-diagnostics.mjs [--check]
 *   --check  rewrites nothing: exits 1 when the document differs from what the sources say.
 */

import { readFileSync, writeFileSync, readdirSync, statSync, existsSync } from "node:fs";
import { join, dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// PRAGMATIC_DIAGNOSTICS_ROOT lets the test drive the script over a throwaway tree. Unset in
// every real run.
const ROOT = process.env.PRAGMATIC_DIAGNOSTICS_ROOT
  ? resolve(process.env.PRAGMATIC_DIAGNOSTICS_ROOT)
  : join(dirname(fileURLToPath(import.meta.url)), "..");
const OUT = join(ROOT, "docs", "diagnostics.md");
const SKIP = new Set(["obj", "bin", "artifacts", ".agentflow", ".git", ".internals", "node_modules", "TestResults"]);

/** The range each module owns. */
const RANGES = [
  [1, 99, "Result"], [100, 199, "Ensure"], [200, 299, "Validation"], [300, 399, "Mapping"],
  // The ids free inside 0400-0449 (PRAG0402, PRAG0417) are retired, and ActionsDiagnostics
  // forbids their reuse: a reused id silently changes what somebody's suppression means.
  [400, 499, "Actions"], [500, 599, "Endpoints"], [600, 699, "Persistence.EFCore"],
  [700, 799, "Persistence.Query"], [800, 899, "Messaging"], [900, 999, "Temporal"],
  [1000, 1099, "Identity / Authorization"], [1100, 1199, "Persistence.Ownership"],
  [1400, 1499, "DependencyInjection"], [1600, 1699, "Composition"], [1700, 1799, "Caching"],
  [1800, 1899, "Internationalization"], [1900, 1999, "Documents"], [2000, 2099, "Configuration"],
  [2100, 2149, "Notifications"], [2200, 2249, "Patch"], [2300, 2349, "Client"], [2350, 2399, "Testing"], [2400, 2449, "Logging"], [2500, 2549, "Jobs"],
  [2600, 2699, "Traits + Resource"], [2700, 2749, "ValueObject"], [2750, 2799, "Lifecycle"],
  [2800, 2899, "Serialization / AOT"], [2900, 2999, "Privacy"], [9000, 9099, "Generator infrastructure"],
];

// Every descriptor, however it is written: any accessibility (PRAG9000 is `private`), and an
// initializer that is `new`, `DiagnosticFactory.*`, or a factory method declared in the same file.
//
// The second group captures the severity only for `DiagnosticFactory.Name`, where the name IS the
// severity. For a local factory the severity lives elsewhere: at the call site when passed, otherwise
// as the default value in the method's signature — see severityOf.
const DECL =
  /(?:public|internal|private|protected)?\s*static\s+readonly\s+DiagnosticDescriptor\s+(\w+)\s*=\s*(?:DiagnosticFactory\.(\w+)|new\b|([A-Za-z_]\w*)\s*(?=\())\s*(?:<[^>]*>)?\s*\(/g;

/**
 * The severity of a descriptor built by a factory declared in the same file.
 *
 * A call such as `Create("PRAG0200", …)` does not name its severity: it inherits the method's
 * default parameter, which is read here.
 */
function severityOf(text, tail, factoryName) {
  const atCallSite = /DiagnosticSeverity\.(\w+)/.exec(tail)?.[1];
  if (atCallSite) return atCallSite;
  if (!factoryName) return "?";

  const signature = new RegExp(
    `static\\s+DiagnosticDescriptor\\s+${factoryName}\\s*\\(([^)]*)\\)`, "s").exec(text);
  return /DiagnosticSeverity\s+\w+\s*=\s*DiagnosticSeverity\.(\w+)/.exec(signature?.[1] ?? "")?.[1] ?? "?";
}

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (SKIP.has(entry)) continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) yield* walk(path);
    else if (entry.endsWith(".cs")) yield path;
  }
}

const records = [];
for (const path of walk(ROOT)) {
  const text = readFileSync(path, "utf8");
  if (!text.includes("DiagnosticDescriptor")) continue;

  // Where each declaration starts, so a descriptor's tail stops at the next one: a window reaching
  // into the following descriptor would read its explicit severity as this one's.
  const starts = [...text.matchAll(DECL)].map((d) => d.index);

  DECL.lastIndex = 0;
  let m;
  while ((m = DECL.exec(text)) !== null) {
    const next = starts.find((s) => s > m.index) ?? text.length;
    const end = Math.min(m.index + m[0].length + 1400, next);
    const tail = text.slice(m.index + m[0].length, end);
    const id = /"(PRAG[0-9A-Za-z]+)"/.exec(tail);
    if (!id) continue;

    const after = tail.slice(id.index + id[0].length);
    const title = /"((?:[^"\\]|\\.)*)"/.exec(after);
    const severity = m[2] ?? severityOf(text, tail, m[3]);

    records.push({
      id: id[1],
      symbol: m[1],
      severity,
      title: (title?.[1] ?? "").replace(/\\"/g, '"'),
      file: relative(ROOT, path).replaceAll("\\", "/"),
      line: text.slice(0, m.index).split("\n").length,
    });
  }
}

const numeric = (id) => (/^PRAG(\d+)$/.exec(id) ? Number(/^PRAG(\d+)$/.exec(id)[1]) : null);
const rangeOf = (id) => {
  const n = numeric(id);
  if (n === null) return id.startsWith("PRAGS") ? "Suppressor" : null;
  return RANGES.find(([lo, hi]) => n >= lo && n <= hi)?.[2] ?? null;
};

records.sort((a, b) =>
  (numeric(a.id) ?? 1e9) - (numeric(b.id) ?? 1e9) || a.id.localeCompare(b.id) || a.file.localeCompare(b.file));

const byId = new Map();
for (const r of records) {
  if (!byId.has(r.id)) byId.set(r.id, []);
  byId.get(r.id).push(r);
}
const dupes = [...byId].filter(([, v]) => v.length > 1);
const orphans = [...byId.keys()].filter((id) => rangeOf(id) === null);

const OUTSIDE = "Outside every declared range";
const groups = new Map();
for (const [id, rs] of byId) {
  const key = rangeOf(id) ?? OUTSIDE;
  if (!groups.has(key)) groups.set(key, []);
  groups.get(key).push([id, rs]);
}

const L = [];
const pad = (n) => `PRAG${String(n).padStart(4, "0")}`;

L.push("# PRAG diagnostics dictionary", "");
L.push("Every `DiagnosticDescriptor` declared in the repository, extracted from the code rather than written by hand.");
L.push("What each diagnostic means and how to fix it is on the [diagnostics reference](../site/docs/src/content/docs/reference/diagnostics.md).", "");
L.push("> **Regenerate, do not edit.** `node scripts/sync-diagnostics.mjs`. When this document and the code");
L.push("> disagree, the document is stale.", "");
L.push("## Summary", "", "| | |", "|---|---|");
L.push(`| Declared descriptors | **${records.length}** |`);
L.push(`| Distinct IDs | **${byId.size}** |`);
L.push(`| Collisions | **${dupes.length}** |`);
L.push(`| IDs outside the declared ranges | **${orphans.length}** |`, "");

if (dupes.length) {
  L.push("## ⚠️ Collisions", "");
  L.push("Two descriptors with the same ID. Roslyn treats them as one diagnostic: **suppressing one**");
  L.push("**suppresses the other**, and which severity wins cannot be decided by reading either declaration.", "");
  L.push("| ID | Symbol | Severity | Declared in |", "|---|---|---|---|");
  for (const [id, rs] of dupes)
    for (const r of rs) L.push(`| \`${id}\` | \`${r.symbol}\` | ${r.severity} | \`${r.file}:${r.line}\` |`);
  L.push("");
}

if (orphans.length) {
  L.push("## IDs outside the declared ranges", "");
  L.push("Not necessarily an error: the range may not be registered in this script yet.", "");
  L.push("| ID | Symbol | Declared in |", "|---|---|---|");
  for (const id of orphans)
    for (const r of byId.get(id)) L.push(`| \`${id}\` | \`${r.symbol}\` | \`${r.file}:${r.line}\` |`);
  L.push("");
}

L.push("## By range", "");

const order = [...RANGES.map(([, , n]) => n), "Suppressor", OUTSIDE];
for (const name of order) {
  const items = groups.get(name);
  if (!items) continue;
  const range = RANGES.find(([, , n]) => n === name);
  const span = range ? ` · range \`${pad(range[0])}\`–\`${pad(range[1])}\`` : "";

  L.push(`### ${name}: ${items.length} in use${span}`, "");
  L.push("| ID | Symbol | Severity | Title |", "|---|---|---|---|");
  for (const [id, rs] of items)
    for (const r of rs)
      L.push(`| \`${id}\`${rs.length > 1 ? " ⚠️" : ""} | \`${r.symbol}\` | ${r.severity} | ${r.title} |`);
  L.push("");

  const nums = items.map(([id]) => numeric(id)).filter((n) => n !== null);
  if (range && nums.length) {
    const used = new Set(nums);
    const lo = Math.min(...used), hi = Math.max(...used);
    const free = [];
    for (let x = lo; x <= hi; x++) if (!used.has(x)) free.push(x);
    if (free.length) L.push(`*Free inside the used span:* ${free.map((x) => `\`${pad(x)}\``).join(", ")}.`, "");
    L.push(hi < range[1] ? `*First free after the last used:* \`${pad(hi + 1)}\`.` : "*Range exhausted.*", "");
  }
}

const content = L.join("\n") + "\n";

if (process.argv.includes("--check")) {
  const current = existsSync(OUT) ? readFileSync(OUT, "utf8") : "";
  if (current !== content) {
    console.error("FAIL docs/diagnostics.md does not match the sources — node scripts/sync-diagnostics.mjs");
    process.exit(1);
  }
  console.log(`ok ${records.length} descriptors, ${dupes.length} collisions`);
} else {
  writeFileSync(OUT, content, "utf8");
  console.log(`wrote docs/diagnostics.md — ${records.length} descriptors, ${byId.size} IDs, ${dupes.length} collisions`);
  for (const [id] of dupes) console.log(`  collision: ${id}`);
}
