#!/usr/bin/env node
/**
 * skill-coverage.mjs — which of the framework's declarative surface the skills never mention.
 *
 *   node scripts/skill-coverage.mjs [--all]
 *
 * A consumer discovers Pragmatic through attributes: they are what you write in your own code.
 * An attribute no skill names is, for anyone who installs the packages, a feature that does not
 * exist — [Autocomplete] generates a complete search endpoint from one property, and until a skill
 * names it nobody can find it. A consumer scenario cannot find it either: an agent does not search
 * for what it has no reason to suspect.
 *
 * The match is deliberately GENEROUS — the bare name anywhere in any skill file counts as covered —
 * so the list it prints is a floor, not an estimate. Anything reported as missing really is missing.
 *
 * BUDGET is a ratchet, not a target. The gate fails when the count goes ABOVE it, so a new
 * attribute cannot be born invisible; lower it whenever documentation
 * lands. It is never raised to make a build pass — raising it is the one change that defeats the
 * whole mechanism.
 */
import { execSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

export const BUDGET = 3;

/**
 * @returns {{ total: number, missing: {name: string, module: string}[] }}
 */
export function skillCoverage() {
  const root = process.cwd();
  const skillsDir = join(root, "marketplace", "plugins", "pragmatic-design", "skills");

  const walk = (dir) => readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const p = join(dir, e.name);
    return e.isDirectory() ? walk(p) : p.endsWith(".md") ? [p] : [];
  });
  const skillText = walk(skillsDir).map((f) => readFileSync(f, "utf-8")).join("\n");

  // Public attributes under */src/, excluding the generator's own internals, samples and tests.
  // `git ls-files` reads the index while the loop below reads the disk, so a file deleted but not
  // yet staged is listed and cannot be opened, and an unhandled ENOENT would kill the gate instead of
  // reporting anything. A removed attribute is simply not part of the surface.
  //
  // ⚠️ The index cuts the other way too: an attribute that is written but not yet added is NOT
  // counted, so this ratchet reads green on a tree that already contains it — green while untracked,
  // over budget the moment it is committed. The count belongs to the commit, not to the working tree, and a green
  // here says nothing about a file `git status` still lists as untracked.
  const files = execSync('git ls-files "*/src/*Attribute.cs"', { encoding: "utf-8" })
    .trim().split("\n").filter(Boolean)
    .filter((f) => !/SourceGenerator|Analyzers|samples|tests|templates/.test(f))
    .filter((f) => existsSync(join(root, f)));

  const found = new Map();
  for (const f of files) {
    // Comment lines dropped first. An attribute written as an EXAMPLE inside a /// block —
    // "public sealed class FiscalCodeAttribute : ValidationAttribute" in ValidationAttribute.cs —
    // was counted as public surface, so the ratchet measured a class that does not exist and the
    // budget carried a slot for it.
    const src = readFileSync(join(root, f), "utf-8")
      .split("\n").filter((l) => !/^\s*(\/\/|\/\*|\*)/.test(l)).join("\n");
    for (const m of src.matchAll(/public sealed class (\w+)Attribute(?:<[^>]*>)?\s*:/g)) {
      // Generic and non-generic forms of the same attribute are one feature.
      if (!found.has(m[1])) found.set(m[1], f.split("/")[0]);
    }
  }

  const missing = [...found.entries()]
    .filter(([name]) => !new RegExp(`\\b${name}\\b`).test(skillText))
    .map(([name, module]) => ({ name, module }))
    .sort((a, b) => a.module.localeCompare(b.module) || a.name.localeCompare(b.name));

  return { total: found.size, missing };
}

// --- CLI ---------------------------------------------------------------------------------------

// pathToFileURL, not string concatenation: on Windows a hand-built "file://C:/..." has two slashes
// where import.meta.url has three, the comparison fails, and the CLI silently does nothing while
// exiting 0 — a gate step that reports success by never running.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { total, missing } = skillCoverage();

  console.log(`public declarative surface: ${total} attributes`);
  console.log(`named by at least one skill: ${total - missing.length}`);
  console.log(`NEVER named: ${missing.length} (${Math.round((missing.length / total) * 100)}%) · budget ${BUDGET}\n`);

  let current = "";
  for (const r of missing) {
    if (r.module !== current) { current = r.module; console.log(`  ${current.replace("Pragmatic.", "")}`); }
    console.log(`      [${r.name}]`);
  }

  if (missing.length > BUDGET) {
    console.log(`\nover the budget of ${BUDGET}: a new feature that is already invisible.`);
    process.exit(1);
  }
}
