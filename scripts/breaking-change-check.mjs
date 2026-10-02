#!/usr/bin/env node
/**
 * breaking-change-check — reads a git diff and flags changes that could break a public contract,
 * as opposed to changes that only touch behaviour behind it. Run it before committing: anything it
 * reports is a change someone else has to be told about.
 *
 * The heuristics are deliberately generous. A false positive costs a glance; a missed break costs
 * whoever depended on it.
 *
 *   - a public member removed (type, property, method)
 *   - a public type's constructor signature changed
 *   - a public property or field changing type (Guid -> string)
 *   - AttributeTargets changed on an attribute
 *   - a public method's parameter types or count changed
 *   - an authoritative property replaced by a delegating one (Id { get; set; } -> Id => Other)
 *
 *   node scripts/breaking-change-check.mjs                     against HEAD~
 *   node scripts/breaking-change-check.mjs --base <ref>        against another ref
 *   node scripts/breaking-change-check.mjs --paths <p1> <p2>   only these paths
 *
 * Exit code: 0 nothing found, 1 potential breaks reported, 2 the check itself failed.
 */
import { execFileSync } from "node:child_process";

const args = process.argv.slice(2);
const baseIdx = args.indexOf("--base");
const base = baseIdx >= 0 ? args[baseIdx + 1] : "HEAD~";
const pathsIdx = args.indexOf("--paths");
const paths = pathsIdx >= 0 ? args.slice(pathsIdx + 1) : [];

let diff;
try {
  const argv = ["diff", base, "HEAD", "--unified=0", "--", ...paths];
  if (paths.length === 0) argv.length = 4;
  diff = execFileSync("git", argv, { encoding: "utf8", maxBuffer: 32 * 1024 * 1024 });
} catch (e) {
  console.error("git diff failed:", e.message);
  process.exit(2);
}

const findings = [];
const lines = diff.split(/\r?\n/);
let currentFile = "";
for (const line of lines) {
  const f = line.match(/^\+\+\+\s+b\/(.+)$/);
  if (f) { currentFile = f[1]; continue; }
  if (!currentFile.endsWith(".cs")) continue;

  // -public X Y(...)  a public member removed
  if (/^-\s*public\s+(class|struct|record|interface|enum)\s+\w+/.test(line)) findings.push({ file: currentFile, kind: "removed-public-type", line });
  if (/^-\s*public\s+\w+(\s*<[^>]+>)?\s+\w+\s*[({]/.test(line)) findings.push({ file: currentFile, kind: "removed-public-member", line });

  // Cambio AttributeUsage targets
  if (/AttributeUsage\(AttributeTargets\./.test(line) && /^[-+]/.test(line)) findings.push({ file: currentFile, kind: "attribute-targets-changed", line });

  // Property tipo cambiato (public X Name => public Y Name)
  if (/^[-+]\s*public\s+\w+\??\s+\w+\s*{\s*get/.test(line)) findings.push({ file: currentFile, kind: "public-property-signature-changed", line });

  // Property authoritative swap (es. `Id { get; set; }` -> `Id => Other` o viceversa)
  if (/^[-+]\s*public\s+\w+\??\s+Id\s*(=>|{\s*get)/.test(line)) findings.push({ file: currentFile, kind: "id-property-shape-changed", line });

  // Cambio ctor signature
  if (/^[-+]\s*public\s+\w+\s*\([^)]*\)/.test(line) && !/\/\//.test(line)) {
    const isCtor = line.match(/public\s+(\w+)\s*\(/);
    if (isCtor && currentFile.endsWith(`${isCtor[1]}.cs`)) findings.push({ file: currentFile, kind: "constructor-signature-changed", line });
  }
}

const grouped = {};
for (const f of findings) (grouped[f.kind] ||= []).push(f);
const summary = {
  base,
  files_inspected: [...new Set(lines.filter(l => l.startsWith("+++ b/")).map(l => l.slice(6)))].filter(f => f.endsWith(".cs")).length,
  total_findings: findings.length,
  by_kind: Object.fromEntries(Object.entries(grouped).map(([k, v]) => [k, v.length])),
  findings: findings.slice(0, 30),
};
console.log(JSON.stringify(summary, null, 2));
process.exit(findings.length > 0 ? 1 : 0);
