#!/usr/bin/env node
/**
 * sync-slnx-docs.mjs — Regenerates the `/docs/…` solution folders in the .slnx files
 * from the REAL markdown on disk, so the docs shown in Visual Studio never go stale.
 *
 * - Pragmatic.Design.slnx: `/docs/` (repo root docs + subfolders) plus
 *   `/docs/{Category}/{Module}/` per module (README, CHANGELOG, SPECIFICATION, docs/**).
 *   Categories come from site/scripts/module-map.mjs (same grouping as the docs site).
 * - Every module slnx (Pragmatic.X/Pragmatic.X.slnx): its own `/docs/` folders.
 *
 * All existing `/docs/…` folders are replaced — never edit them by hand.
 *
 * Usage: node scripts/sync-slnx-docs.mjs
 */

import { readdirSync, readFileSync, writeFileSync, existsSync, statSync } from "node:fs";
import { join, dirname, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { MODULE_MAP } from "../site/scripts/module-map.mjs";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const SKIP_DIRS = new Set(["bin", "obj", "node_modules", "packages", ".git", "dist", "TestResults"]);

const CATEGORY_FOLDER = {
  foundation: "Foundation",
  core: "Core",
  capabilities: "Capabilities",
  infrastructure: "Infrastructure",
  compliance: "Compliance",
  "documents-media": "Documents",
  "medium-blocks": "MediumBlocks",
  platform: "Platform",
  sg: "SourceGenerator",
};

/** Recursively collects .md files under dir, returning repo-relative POSIX paths. */
function collectMd(dir) {
  if (!existsSync(dir)) return [];
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (!SKIP_DIRS.has(entry)) out.push(...collectMd(full));
    } else if (entry.endsWith(".md")) {
      out.push(full);
    }
  }
  return out.sort();
}

const posix = (p) => p.replaceAll("\\", "/");
const xml = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;");

/** folderName ("/docs/X/") → list of file paths relative to the slnx location. */
function renderFolders(folders) {
  const lines = [];
  for (const [name, files] of folders) {
    if (files.length === 0) continue;
    lines.push(`  <Folder Name="${xml(name)}">`);
    // NB: the slnx tag for solution items is <File> — <SolutionItem> is silently
    // ignored by Visual Studio (the historical hand-written block used it: nothing showed up).
    for (const f of files) lines.push(`    <File Path="${xml(posix(f))}" />`);
    lines.push(`  </Folder>`);
  }
  return lines.join("\n");
}

/** Groups md files (relative to baseDir) into slnx folders under prefix, one per subdirectory. */
function groupByDir(files, baseDir, prefix) {
  const groups = new Map();
  for (const f of files) {
    const rel = posix(relative(baseDir, f));
    const dir = rel.includes("/") ? rel.substring(0, rel.lastIndexOf("/")) : "";
    const folder = dir === "" ? prefix : `${prefix}${dir}/`;
    if (!groups.has(folder)) groups.set(folder, []);
    groups.get(folder).push(f);
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
}

/** Removes every `/docs/…` Folder block from slnx content. */
function stripDocsFolders(content) {
  return content
    .replace(/[ \t]*<Folder Name="\/docs\/[^"]*"(?: \/>|>[\s\S]*?<\/Folder>)\r?\n/g, "")
    // A module's docs live under the module itself (/Pragmatic.X/docs/…), so they have to be
    // stripped from there too — otherwise every run appends a second copy.
    .replace(/[ \t]*<Folder Name="\/Pragmatic\.[^"]*\/docs\/[^"]*"(?: \/>|>[\s\S]*?<\/Folder>)\r?\n/g, "")
    // `DOCS -->` has a space before the closing marker, so a pattern demanding `DOCS-->` never
    // strips the banner and every run adds another. Match anything up to the close, not just the word.
    .replace(/[ \t]*<!--[^\n]*(?:═+[^\n]*DOCS|docs \(auto-generated)[^\n]*-->\r?\n/g, "")
    .replace(/[ \t]*<!-- docs -->\r?\n/g, "")
    // The block is inserted after a blank separator line, and stripping it leaves that line
    // behind — so each run added one more. Collapse them back to none.
    .replace(/(<\/Configurations>\r?\n)(?:[ \t]*\r?\n)+/, "$1");
}

// ─── Per-module slnx ─────────────────────────────────────────────────────────
const modules = readdirSync(ROOT)
  .filter((d) => d.startsWith("Pragmatic.") && statSync(join(ROOT, d)).isDirectory())
  .sort();

let moduleSlnxCount = 0;
for (const mod of modules) {
  const slnxPath = join(ROOT, mod, `${mod}.slnx`);
  if (!existsSync(slnxPath)) continue;

  const modDir = join(ROOT, mod);
  const rootFiles = ["README.md", "CHANGELOG.md", "SPECIFICATION.md"]
    .map((f) => join(modDir, f))
    .filter(existsSync);
  const docFiles = collectMd(join(modDir, "docs"));

  // README/CHANGELOG/SPECIFICATION + docs/*.md under /docs/; docs subdirs become subfolders.
  const merged = new Map();
  merged.set("/docs/", rootFiles.map((f) => relative(modDir, f)));
  for (const [name, files] of groupByDir(docFiles, join(modDir, "docs"), "/docs/")) {
    if (!merged.has(name)) merged.set(name, []);
    merged.get(name).push(...files.map((f) => relative(modDir, f)));
  }

  const block = `  <!-- docs (auto-generated: node scripts/sync-slnx-docs.mjs) -->\n${renderFolders([...merged.entries()])}\n`;
  let content = stripDocsFolders(readFileSync(slnxPath, "utf8"));
  content = content.replace(/<Solution>\r?\n/, (m) => m + block);
  writeFileSync(slnxPath, content, "utf8");
  moduleSlnxCount++;
}

// ─── Root slnx ───────────────────────────────────────────────────────────────
const rootSlnxPath = join(ROOT, "Pragmatic.Design.slnx");
let root = stripDocsFolders(readFileSync(rootSlnxPath, "utf8"));

const rootFolders = [];

// Repo-level docs: the solution shows the product and reference documentation.
const REPO_DOCS_TOP = ["README.md", "docs/ROADMAP.md", "docs/LICENSING.md", "docs/README.md", "docs/CONVENTIONS.md", "docs/TESTING.md", "docs/diagnostics.md"];
const REPO_DOCS_DIRS = ["howto", "security"];

rootFolders.push(["/docs/", REPO_DOCS_TOP.filter((f) => existsSync(join(ROOT, f)))]);
for (const dir of REPO_DOCS_DIRS) {
  const files = collectMd(join(ROOT, "docs", dir));
  for (const [name, groupFiles] of groupByDir(files, join(ROOT, "docs"), "/docs/"))
    rootFolders.push([name, groupFiles.map((f) => relative(ROOT, f))]);
}

// A module's docs sit under the module's own solution folder, beside its projects, not in a
// parallel /docs/{Category}/{Module}/ branch: that would mean looking in two places for one module,
// its code here and everything written about it there. The category grouping is given up for
// navigability, and the docs site groups by category.
//
// Nothing moves on disk. Solution folders are virtual, so this is purely where the <File>
// entries are emitted.
for (const mod of modules) {
  const modDir = join(ROOT, mod);
  const rootFiles = ["README.md", "CHANGELOG.md", "SPECIFICATION.md"]
    .map((f) => join(modDir, f))
    .filter(existsSync)
    .map((f) => relative(ROOT, f));
  const docFiles = collectMd(join(modDir, "docs"));
  if (rootFiles.length === 0 && docFiles.length === 0) continue;

  const prefix = `/${mod}/docs/`;
  const merged = new Map([[prefix, [...rootFiles]]]);
  for (const [name, files] of groupByDir(docFiles, join(modDir, "docs"), prefix)) {
    if (!merged.has(name)) merged.set(name, []);
    merged.get(name).push(...files.map((f) => relative(ROOT, f)));
  }
  rootFolders.push(...merged.entries());
}

// Showcase docs
const showcaseDir = join(ROOT, "examples", "showcase");
const showcaseFiles = [join(showcaseDir, "README.md"), ...collectMd(join(showcaseDir, "docs"))]
  .filter(existsSync)
  .map((f) => relative(ROOT, f));
if (showcaseFiles.length > 0) rootFolders.push(["/docs/Examples/", showcaseFiles]);

const rootBlock =
  `  <!-- ══════════════════════════════════════════════════════════ DOCS -->\n` +
  `  <!-- docs (auto-generated: node scripts/sync-slnx-docs.mjs) -->\n` +
  `${renderFolders(rootFolders)}\n`;
root = root.replace(/<\/Configurations>\r?\n/, (m) => m + "\n" + rootBlock);
writeFileSync(rootSlnxPath, root, "utf8");

const total = rootFolders.reduce((n, [, files]) => n + files.length, 0);
console.log(`✅ Root slnx: ${total} docs in ${rootFolders.length} folders; ${moduleSlnxCount} module slnx updated.`);
