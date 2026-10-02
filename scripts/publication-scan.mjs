#!/usr/bin/env node
/**
 * publication-scan — everything about "can this be published" that is decidable by looking, so that
 * reading a file is spent on what looking cannot decide.
 *
 * Each check reports the lines it matched, and reports separately when it could not run. A check
 * that found nothing and a check that never executed produce the same silence otherwise, and only
 * one of them is good news.
 *
 *   node scripts/publication-scan.mjs                  whole repository
 *   node scripts/publication-scan.mjs Pragmatic.Jobs   one module
 *   node scripts/publication-scan.mjs --json           machine-readable
 *
 * Exit code: 0 nothing found, 1 findings, 2 the scan itself failed.
 */
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { TRACKER_REFERENCE, TRACKER_REFERENCE_IGNORE } from "./lib/tracker-reference.mjs";

const args = process.argv.slice(2);
const asJson = args.includes("--json");
const scope = args.find(a => !a.startsWith("--"));

// A comment line in C# or JavaScript: where a date or a "used to" is prose, not data.
const COMMENT = /^\s*(\/\/|\/\*|\*|\/\/\/)/;

// Written so a match means something. An earlier attempt at the Italian check counted `serve` and
// `sono`, which are also English words, and reported sixteen findings where there was one.
const CHECKS = [
  {
    id: "italian-comment",
    what: "Comment written in Italian",
    why: "The repository's language is English.",
    files: /\.(cs|mjs|js|ts)$/,
    line: /^\s*(\/\/|\/\*|\*|\/\/\/)/,
    match: /\b(perch[éè]|poich[éè]|cio[èe]|quindi|invece|oppure|soltanto|affinch[éè]|nonch[éè]|dell'|nell'|sull'|all'|un'[aeiou])\b/i,
  },
  {
    id: "personal-data",
    what: "Personal address or a path from someone's machine",
    why: "A maintainer's inbox is not a project contact, and a local path means nothing to a reader.",
    files: /\.(cs|mjs|js|ts|md|json|yml|yaml|props|targets|csproj)$/,
    match: /[A-Za-z0-9._%+-]+@(gmail|hotmail|outlook|yahoo|libero|icloud)\.[a-z]+|[A-Z]:\\Users\\[A-Za-z0-9._-]+|\/home\/[a-z][a-z0-9._-]+\//,
    ignore: /InlineData|MemberData|example\.(com|org)/,
  },
  {
    id: "unpublished-reference",
    what: "Link into something that will not be published",
    why: "These directories do not exist for a reader, so the reference points at nothing.",
    files: /\.(cs|mjs|js|ts|md|json|yml|yaml)$/,
    match: /(^|[\s"'`(\[])(\.internals?\/|docs\/audit\/|\.plan\/|\.agentflow\/|module-ledger)/,
  },
  {
    id: "campaign-vocabulary",
    what: "Wording that only means something to whoever was there",
    why: "Wave numbers, finding IDs and sprint references date the text and explain nothing.",
    files: /\.(cs|md)$/,
    match: /\b(W\d{1,2}\b.{0,20}(wave|finding)|wave\s+\d|#S\d-\d|sprint\s+\d|PRAG-?\d+\s+wave)\b/i,
  },
  {
    id: "history-narration",
    what: "How the code came to be, told in its comments or docs",
    why: "A reader needs what is true now. \"It used to…\" and \"Until <date>…\" belong to the commit that changed it.",
    files: /\.(cs|mjs|js|ts|md)$/,
    commentsOnlyIn: /\.(cs|mjs|js|ts)$/,
    // Not the passive "is used to", "can be used to": those describe a purpose, not a past. The
    // participle of a purpose ("the claim type used to match the user") still matches and cannot be
    // told apart by a pattern, so this count is a ceiling; the review of each line is what decides.
    match: /(?<!\b(?:is|are|be|was|were|been|being|get|gets|got|getting|commonly|typically|often|then)\s)\bused to\b|\b(Until|Fino al?) \d{4}-\d\d-\d\d/i,
  },
  {
    id: "tracker-reference",
    what: "A tracker key, a review tag or a section of the internal rulebook",
    why: "PRAG-123, ACT-009, #F5-1, story 11 and §5.2-bis point at documents a reader cannot open. State the rule instead.",
    files: /\.(cs|mjs|js|ts|md|csproj|props|targets|yml|yaml)$/,
    commentsOnlyIn: /\.(cs|mjs|js|ts)$/,
    match: TRACKER_REFERENCE,
    ignore: TRACKER_REFERENCE_IGNORE,
  },
  {
    id: "dated-text",
    what: "A date in a comment or a document",
    why: "\"Measured 2026-09-26\" dates the text the day it is read. State the fact; the date is in the history.",
    files: /\.(cs|mjs|js|ts|md)$/,
    commentsOnlyIn: /\.(cs|mjs|js|ts)$/,
    match: /\b20(2[4-9])-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])\b/,
    // A date in a code sample, or quoted as a value, is data: `app-2026-03-21.log`, "2026-06-01".
    proseOnlyIn: /\.md$/,
    ignore: /["'`][^"'`]*\b20\d\d-\d\d-\d\d/,
    ignoreFiles: /CHANGELOG\.md$/,
  },
  {
    id: "incomplete",
    what: "Work marked as not done",
    why: "Each one has to be a stated deferral or it is a hole. PendingBehaviorException is deliberate and excluded.",
    files: /\.cs$/,
    match: /\b(TODO|FIXME|HACK|XXX)\b|throw new NotImplementedException/,
    // "XXX" in quotes is the ISO 4217 code for "no currency", used as a value.
    ignore: /PendingBehaviorException|\bPending\(|"XXX"/,
  },
  {
    id: "runtime-reflection",
    what: "Reflection at runtime",
    why: "The generator knows the shape at compile time; EF Core internals are the documented exception.",
    files: /\.cs$/,
    // Only when the receiver is plainly a Type. A first version matched any `.GetProperty(`, so it
    // caught `LogContextScope.GetProperty("CorrelationId")` — our own API — and EF's
    // `entityType.GetProperties()`, and reported 526 findings almost none of which were real.
    match: /(typeof\s*\([^)]*\)|\.GetType\s*\(\)|\b[A-Za-z_]*[Tt]ype)\s*\.\s*(GetProperty|GetProperties|GetMethods?|GetField|GetCustomAttributes?|MakeGenericType|MakeGenericMethod|InvokeMember)\s*\(|Activator\.CreateInstance/,
    codeOnly: true,
    // `roomType.GetProperty("id").GetGuid()` reads a JsonElement — a variable named *Type, not a Type.
    ignore: /entityType|IMutableEntityType|modelBuilder|\.GetProperty\("[^"]*"\)\.Get[A-Z]/,
    ignoreFiles: /Analyzers|CodeFixers|SourceGenerator|\.Tests?\/|\/samples\//,
  },
  {
    id: "interpolated-log",
    what: "Log message built by interpolation",
    why: "Logging goes through [LoggerMessage]: an interpolated string allocates and loses structure.",
    files: /\.cs$/,
    match: /Log(Trace|Debug|Information|Warning|Error|Critical)\s*\(\s*\$"/,
  },
  {
    id: "typeof-attribute",
    what: "Attribute taking a typeof argument",
    why: "Attributes are generic here: [MapFrom<Order>], not [MapFrom(typeof(Order))].",
    files: /\.cs$/,
    match: /^\s*\[[A-Z][A-Za-z]*\s*\(\s*typeof\(/,
    // Attributes of the BCL and of BenchmarkDotNet: not ours to make generic.
    ignore: /\[(JsonSerializable|JsonDerivedType|JsonConverter|InlineData|MemberData|ClassData|TypeConverter|DebuggerTypeProxy|Generator|SetsRequiredMembers|SuppressMessage|DynamicDependency|UnconditionalSuppressMessage|Obsolete|CollectionBuilder|FromKeyedServices|Config)\s*\(/,
  },
  {
    id: "possible-secret",
    what: "Literal shaped like a credential",
    why: "Worth a look even in tests — say whether it is real.",
    files: /\.(cs|json|yml|yaml)$/,
    match: /(api[_-]?key|secret|password|token)\s*[:=]\s*["\x27](?=[^"\x27]*[A-Z])(?=[^"\x27]*[a-z])(?=[^"\x27]*\d)[A-Za-z0-9\/+_=-]{24,}["\x27]/i,
    ignore: /xxx|placeholder|YOUR[_-]|changeme|<[a-z-]+>|UseDevelopmentStorage|example|dummy|fake|invalid/i,
  },
];

function tracked(prefix) {
  const out = execFileSync("git", ["ls-files", ...(prefix ? [prefix] : [])], { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 });
  return out.split(/\r?\n/).filter(Boolean);
}

let files;
try {
  files = tracked(scope);
} catch (e) {
  console.error(`publication-scan could not list files: ${e.message}`);
  process.exit(2);
}
if (files.length === 0) {
  console.error(`publication-scan: no tracked files under "${scope ?? "."}" — nothing was scanned.`);
  process.exit(2);
}

const results = CHECKS.map(c => ({ id: c.id, what: c.what, why: c.why, hits: [], scanned: 0, unreadable: [] }));

for (const file of files) {
  let text;
  try {
    text = readFileSync(file, "utf8");
  } catch {
    for (const r of results) r.unreadable.push(file);
    continue;
  }
  if (text.includes(" ")) continue; // binary
  const lines = text.split(/\r?\n/);

  CHECKS.forEach((check, i) => {
    if (!check.files.test(file)) return;
    if (check.ignoreFiles?.test(file)) return;
    results[i].scanned++;
    const commentsOnly = check.commentsOnlyIn?.test(file);
    const proseOnly = check.proseOnlyIn?.test(file);
    let inFence = false;
    lines.forEach((text, n) => {
      if (/^\s*(```|~~~)/.test(text)) inFence = !inFence;
      if (proseOnly && inFence) return;
      if (check.line && !check.line.test(text)) return;
      if (commentsOnly && !COMMENT.test(text)) return;
      if (check.codeOnly && COMMENT.test(text)) return;
      if (!check.match.test(text)) return;
      if (check.ignore?.test(text)) return;
      results[i].hits.push({ file, line: n + 1, text: text.trim().slice(0, 120) });
    });
  });
}

const total = results.reduce((s, r) => s + r.hits.length, 0);

if (asJson) {
  console.log(JSON.stringify({ scope: scope ?? ".", files: files.length, total, checks: results }, null, 2));
  process.exit(total > 0 ? 1 : 0);
}

console.log(`publication-scan — ${files.length} tracked files under ${scope ?? "the repository"}\n`);
for (const r of results) {
  if (r.scanned === 0) {
    console.log(`  ??  ${r.id} — no file matched this check's file types, so it did not run`);
    continue;
  }
  if (r.hits.length === 0) {
    console.log(`  ok  ${r.id} — ${r.scanned} files, nothing found`);
    continue;
  }
  console.log(`\n  !!  ${r.id} — ${r.hits.length} in ${r.scanned} files`);
  console.log(`      ${r.what}. ${r.why}`);
  for (const h of r.hits.slice(0, 12)) console.log(`      ${h.file}:${h.line}  ${h.text}`);
  if (r.hits.length > 12) console.log(`      … and ${r.hits.length - 12} more (use --json for all)`);
}
const unreadable = [...new Set(results.flatMap(r => r.unreadable))];
if (unreadable.length) console.log(`\n  ??  ${unreadable.length} files could not be read and were NOT scanned`);
console.log(`\n${total === 0 ? "clean" : `${total} findings`}`);
process.exit(total > 0 ? 1 : 0);
