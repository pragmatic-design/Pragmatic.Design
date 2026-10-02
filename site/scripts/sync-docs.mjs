#!/usr/bin/env node
/**
 * sync-docs.mjs — Syncs module documentation into the Starlight docs site.
 *
 * Reads README.md + docs/*.md from each Pragmatic.* module and copies them
 * into site/docs/src/content/docs/modules/{slug}/ with Starlight frontmatter.
 *
 * Usage: node site/scripts/sync-docs.mjs
 * Or:    pnpm sync:docs (from site/)
 */

import { readdir, readFile, writeFile, mkdir, rm } from "node:fs/promises";
import { join, basename, dirname, sep, posix } from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = join(__dirname, "..", "..");
const DOCS_OUT = join(__dirname, "..", "docs", "src", "content", "docs", "modules");

import { MODULE_MAP } from "./module-map.mjs";

/**
 * --check regenerates into memory and compares against what is on disk, changing nothing and
 * failing when they differ. It exists because the copies under site/docs are generated from
 * {Module}/docs/ and nothing compared them: correcting twenty source documents silently left twenty
 * published copies stale, and no build, test or gate said so.
 */
const CHECK = process.argv.includes("--check");
const generated = new Map();

/** Writes, or in --check records what would have been written. */
async function emit(path, content) {
  if (CHECK) { generated.set(path, content); return; }
  await writeFile(path, content, "utf-8");
}

/** Removes the output directory — never in --check, which must not touch the tree. */
async function resetDir(dir) {
  if (CHECK) return;
  if (existsSync(dir)) await rm(dir, { recursive: true });
  await mkdir(dir, { recursive: true });
}

// Always emit LF, regardless of the source file's line endings. Keeps generated site
// content stable across OSes (no CRLF/LF churn on Windows). Pairs with `*.md eol=lf`
// in .gitattributes.
const toLf = (s) => s.replace(/\r\n/g, "\n");

// ⚠️ Sources are normalised when READ, not only the output when written. The rules below are
// written for LF: removing the H1 is /^#\s+.+\n*/, and `.` does not match `\r`, so on a CRLF source
// the title went and its `\r\n\r\n` stayed — two blank lines after the frontmatter that toLf then
// kept. Windows working copies can hold CRLF .md files whatever .gitattributes says, Linux ones
// cannot: the pages were generated on Windows with the blank lines, and the CI's --check on Linux
// regenerated them without — "drifted" on ~60 pages, and the gate stopped in 0.3 s on every push.
// A byte-order mark is dropped too: an editor that saves UTF-8 with one puts it before the `#`, the H1
// is then not at the start of its line, and the page was titled with its file name ("02-entity-system")
// with an empty description and the H1 left in the body.
export const normalizeSource = (text) => toLf(text.replace(/^﻿/, ""));
const readSource = async (path) => normalizeSource(await readFile(path, "utf-8"));

// Extract title from first H1 in markdown
export function extractTitle(content) {
  const match = content.match(/^#\s+(.+)$/m);
  return match ? match[1].trim() : null;
}

// Extract description from first paragraph after H1. A description is plain text in a meta tag, so a
// link keeps its text and loses its target: fixLinks rewrites the body only, and a relative target left
// in the frontmatter failed the Docs workflow's unresolved-link check.
function extractDescription(content) {
  const lines = content.split("\n");
  let foundH1 = false;
  for (const line of lines) {
    if (line.startsWith("# ")) {
      foundH1 = true;
      continue;
    }
    if (foundH1 && line.trim() && !line.startsWith("#") && !line.startsWith("---")) {
      return line.trim().replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1").substring(0, 160);
    }
  }
  return "";
}

// Add Starlight frontmatter to markdown content
function addFrontmatter(content, { title, description, order, isOverview, editUrl }) {
  // Remove existing frontmatter if present
  let body = content;
  if (body.startsWith("---")) {
    const end = body.indexOf("---", 3);
    if (end !== -1) {
      body = body.substring(end + 3).trim();
    }
  }

  // Remove the first H1 (Starlight generates it from frontmatter title)
  body = body.replace(/^#\s+.+\n*/m, "");

  const fm = [
    "---",
    `title: "${title.replace(/"/g, '\\"')}"`,
    `description: "${description.replace(/"/g, '\\"')}"`,
  ];

  // Point "Edit this page" at the canonical repo source, not the generated copy.
  if (editUrl) {
    fm.push(`editUrl: ${editUrl}`);
  }

  if (order !== undefined) {
    fm.push(`sidebar:`);
    fm.push(`  order: ${order}`);
    if (isOverview) {
      fm.push(`  label: Overview`);
    }
  }

  fm.push("---");
  fm.push("");

  return fm.join("\n") + body;
}

const REPO_URL = "https://github.com/pragmatic-design/Pragmatic.Design";

// Build a fast "Pragmatic.X" → site slug lookup from MODULE_MAP (handles the SG "../source-generator").
function moduleSlugFor(moduleName) {
  const entry = MODULE_MAP[moduleName];
  if (!entry) return null;
  // The SG entry uses a "../source-generator" slug (sibling section) — map to its real path.
  return entry.slug.startsWith("../") ? entry.slug.slice(3) : `modules/${entry.slug}`;
}

// Fix relative links so they resolve on the published Starlight site (not just on GitHub).
// `sourcePath` is the document's path in the repository ("Pragmatic.Logging/docs/concepts.md"):
// a link the site cannot serve is resolved from it, the way the document meant it.
export function fixLinks(content, moduleSlug, docPath = "", sourcePath = "") {
  let out = content;
  // Where a page of this section lives: "/modules/logging", or "/guides" for the how-to guides.
  const sectionBase = moduleSlug.startsWith("../") ? `/${moduleSlug.slice(3)}` : `/modules/${moduleSlug}`;
  // A README is the section's overview; a bare "x.md" next to it is a repository file, not a page.
  const isDocPage = sourcePath !== "" && !sourcePath.endsWith("/README.md");
  // The folder this doc lives in, relative to docs/ — "how-it-works" or "" at the top.
  const folder = docPath.includes("/") ? docPath.slice(0, docPath.lastIndexOf("/")) : "";

  // 0. Links written from inside a docs subfolder. Handled first, because the generic rules below
  //    would send them to GitHub — correct for an unknown path, wrong for a page the site has.
  if (folder) {
    // 0a. Up to a top-level doc: [text](../interfaces.md#x) → /modules/slug/interfaces/#x
    out = out.replace(
      /\]\(\.\.\/([^)#/]+)\.md(#[^)]*)?\)/g,
      (_, path, anchor) => `](/modules/${moduleSlug}/${path}/${anchor || ""})`
    );

    // 0b. Sibling in the same subfolder: [text](authorization.md) → /modules/slug/how-it-works-authorization/
    out = out.replace(
      /\]\((?:\.\/)?([^)#/.][^)#/]*)\.md(#[^)]*)?\)/g,
      (_, path, anchor) => `](/modules/${moduleSlug}/${folder}-${path}/${anchor || ""})`.toLowerCase()
    );
  }

  // 1. Intra-module docs, including one subfolder level (optional ./ prefix, optional #anchor):
  //    [text](docs/foo.md) → /modules/slug/foo/ · [text](how-it-works/foo.md) → /modules/slug/how-it-works-foo/
  out = out.replace(
    /\]\((?:\.\/)?docs\/([^)#]+)\.md(#[^)]*)?\)/g,
    (_, path, anchor) => `](/modules/${moduleSlug}/${pageSlug(path)}/${anchor || ""})`
  );

  out = out.replace(
    /\]\((?:\.\/)?([a-z0-9-]+\/[^)#/]+)\.md(#[^)]*)?\)/g,
    (_, path, anchor) => `](/modules/${moduleSlug}/${pageSlug(path)}/${anchor || ""})`
  );

  // 1b. Same-dir sibling doc: [text](./common-mistakes.md#x) → /modules/slug/common-mistakes/#x
  out = out.replace(
    /\]\(\.\/([^)#/]+)\.md(#[^)]*)?\)/g,
    (_, path, anchor) => `](/modules/${moduleSlug}/${path}/${anchor || ""})`
  );

  // 1c. The same, written bare, from a doc page: [text](concepts.md#x) → /modules/slug/concepts/#x.
  //     Left relative, it resolved against the page's own URL (/modules/slug/getting-started/) and
  //     named a file that is not there.
  if (isDocPage) {
    out = out.replace(
      /\]\(([A-Za-z0-9][^)#/:\s]*)\.md(#[^)]*)?\)/g,
      (_, path, anchor) => `](${sectionBase}/${pageSlug(path)}/${anchor || ""})`
    );
  }

  // 2. Same-module README (optional #anchor): [text](../README.md#x) → /modules/slug/overview/#x
  out = out.replace(
    /\]\(\.\.\/README\.md(#[^)]*)?\)/g,
    (_, anchor) => `](/modules/${moduleSlug}/overview/${anchor || ""})`
  );

  // 3. Cross-module README (ANY ../ depth): [text](../Pragmatic.X/README.md) → /modules/{slug-of-X}/overview/
  out = out.replace(
    /\]\((?:\.\.\/)+(Pragmatic\.[A-Za-z.]+)\/README\.md\)/g,
    (_, mod) => {
      const slug = moduleSlugFor(mod);
      if (!slug) return `](${REPO_URL}/tree/main/${mod})`; // sub-package or unmapped → repo
      return slug.startsWith("modules/") ? `](/${slug}/overview/)` : `](/${slug}/)`;
    }
  );

  // 4. Cross-module docs (ANY ../ depth): [text](../Pragmatic.X/docs/foo.md) → /modules/{slug-of-X}/foo/
  out = out.replace(
    /\]\((?:\.\.\/)+(Pragmatic\.[A-Za-z.]+)\/docs\/([^)]+)\.md\)/g,
    (_, mod, path) => {
      const slug = moduleSlugFor(mod);
      // Sub-packages (e.g. Pragmatic.Persistence.EFCore) have no own site section → link to GitHub docs.
      // pageSlug, not basename: a subfolder page is flattened into its name, so
      // how-it-works/policies.md is the page how-it-works-policies, not policies.
      return slug && slug.startsWith("modules/")
        ? `](/${slug}/${pageSlug(path)}/)`
        : `](${REPO_URL}/blob/main/${mod}/docs/${path}.md)`;
    }
  );

  // 4b. Cross-module docs FOLDER (ANY depth): [text](../Pragmatic.X/docs/) → /modules/{slug-of-X}/overview/
  out = out.replace(
    /\]\((?:\.\.\/)+(Pragmatic\.[A-Za-z.]+)\/docs\/\)/g,
    (_, mod) => {
      const slug = moduleSlugFor(mod);
      return slug && slug.startsWith("modules/") ? `](/${slug}/overview/)` : `](${REPO_URL}/tree/main/${mod}/docs)`;
    }
  );

  // 5. Repo-root docs (ANY depth): [text](../../docs/foo.md) → GitHub (no site home for these).
  out = out.replace(
    /\]\((?:\.\.\/)+docs\/([^)]+)\.md\)/g,
    (_, path) => `](${REPO_URL}/blob/main/docs/${path}.md)`
  );

  // 6. Catch-all: ANY remaining relative link — ./, ../ or bare (files, folders, with or without
  //    #anchor) → an absolute GitHub URL so nothing 404s on the published site. Well-known forms
  //    above already resolved to site pages; everything else (root files like ../../SECURITY.md,
  //    samples, images, folder links) resolves on GitHub, from where the source document lives.
  const FILE_EXTS = ["md", "markdown", "cs", "csproj", "json", "yml", "yaml", "txt", "sln", "slnx", "xml", "props", "targets", "sh", "ps1", "png", "jpg", "jpeg", "gif", "svg"];
  const IMG_EXTS = ["png", "jpg", "jpeg", "gif", "svg"];
  out = out.replace(/\]\((?![a-z][a-z0-9+.-]*:|\/|#)([^)\s]+)\)/gi, (_, rel) => {
    const hashIdx = rel.indexOf("#");
    const anchor = hashIdx >= 0 ? rel.slice(hashIdx) : "";
    const pathPart = hashIdx >= 0 ? rel.slice(0, hashIdx) : rel;
    const trailingSlash = /\/$/.test(pathPart);
    // Resolved from the source document. Without a source path, or when resolving climbs out of
    // the repository, the old reading stands: the path from the repository root.
    const resolved = sourcePath ? posix.normalize(posix.join(posix.dirname(sourcePath), pathPart)) : "";
    const cleaned = (resolved && !resolved.startsWith("..") ? resolved : pathPart.replace(/^(?:\.\.\/)+/, "").replace(/^\.\//, ""))
      .replace(/\/+$/, "");
    const last = cleaned.split("/").pop() || "";
    const ext = last.includes(".") ? last.split(".").pop().toLowerCase() : "";
    // A dotted DIRECTORY name (e.g. Showcase.Billing) would fool a bare extension check, so resolve
    // "file vs folder" via a known-extension allowlist + trailing slash. Unknown → folder (tree).
    const kind = trailingSlash
      ? "tree"
      : IMG_EXTS.includes(ext)
        ? "raw"
        : FILE_EXTS.includes(ext)
          ? "blob"
          : "tree";
    return `](${REPO_URL}/${kind}/main/${cleaned}${anchor})`;
  });

  return out;
}

// Remove the "Documentation" index table from README (sidebar handles navigation in site)
function removeDocsIndex(content) {
  // Matches: ## Documentation\n\n| ... | ... |\n|...|...|\n| [Guide](docs/...) | ... |\n...
  return content.replace(
    /##\s+Documentation\s*\n+(\|[^\n]*\n){1,2}(\|[^\n]*docs\/[^\n]*\n)*/g,
    ""
  );
}

// Slugify a filename: binding-reference.md → binding-reference
function fileSlug(filename) {
  return basename(filename, ".md").toLowerCase();
}

/**
 * The site page name for a doc path, flattening one level of folder:
 * "how-it-works/identity.md" → "how-it-works-identity.md". The site has a flat page set per
 * module, and this keeps the URL derivable from the path on disk.
 */
function pageName(docPath) {
  return docPath.split("/").join("-").toLowerCase();
}

/** The same, as a URL segment: no extension. */
function pageSlug(docPath) {
  return pageName(docPath).replace(/.md$/, "");
}

// Sort doc files: concepts/getting-started first, common-mistakes/troubleshooting/migration last
function sortDocFiles(files) {
  const priorityOrder = {
    "concepts.md": -100,
    "getting-started.md": -90,
    "api-reference.md": 800,
    "common-mistakes.md": 900,
    "troubleshooting.md": 910,
    "migration.md": 920,
  };

  return [...files].sort((a, b) => {
    const aLower = a.toLowerCase();
    const bLower = b.toLowerCase();
    const aOrder = priorityOrder[aLower] ?? 0;
    const bOrder = priorityOrder[bLower] ?? 0;
    if (aOrder !== bOrder) return aOrder - bOrder;
    return aLower.localeCompare(bLower);
  });
}

async function syncModule(moduleName, config) {
  const moduleDir = join(ROOT, moduleName);
  const readmePath = join(moduleDir, "README.md");
  const docsDir = join(moduleDir, "docs");
  const outDir = join(DOCS_OUT, config.slug);

  if (!existsSync(readmePath)) {
    console.log(`  ⏭ ${moduleName}: no README.md, skipping`);
    return { pages: 0 };
  }

  // Clean and recreate output dir
  await resetDir(outDir);

  let pages = 0;

  // 1. Sync README.md → overview.md
  const readme = await readSource(readmePath);
  const title = extractTitle(readme) || moduleName.replace("Pragmatic.", "");
  const description = extractDescription(readme);
  const overview = addFrontmatter(
    removeDocsIndex(fixLinks(readme, config.slug, "", `${moduleName}/README.md`)),
    {
      title, description, order: 0, isOverview: true,
      editUrl: `${REPO_URL}/edit/main/${moduleName}/README.md`,
    }
  );
  await emit(join(outDir, "overview.md"), toLf(overview));
  pages++;

  // 2. Sync docs/*.md, plus one level of subfolders (docs/how-it-works/identity.md).
  // Subfolder pages are flattened into the module's flat page set, keeping the folder in the
  // name: how-it-works/identity.md → how-it-works-identity. That keeps the URL derivable from
  // the path without teaching the site a nested layout it does not have.
  if (existsSync(docsDir)) {
    const docFiles = await readdir(docsDir, { recursive: true });
    const mdFiles = docFiles
      .map((f) => f.split(sep).join("/"))
      .filter((f) => f.endsWith(".md"));

    // Sort docs intelligently: concepts/getting-started first, common-mistakes/troubleshooting last
    const sortedFiles = sortDocFiles(mdFiles);

    for (let i = 0; i < sortedFiles.length; i++) {
      const file = sortedFiles[i];
      const filePath = join(docsDir, file);
      const content = await readSource(filePath);
      const docTitle = extractTitle(content) || fileSlug(file);
      const docDesc = extractDescription(content);
      const processed = addFrontmatter(
        fixLinks(content, config.slug, file, `${moduleName}/docs/${file}`),
        {
          title: docTitle, description: docDesc, order: i + 1,
          editUrl: `${REPO_URL}/edit/main/${moduleName}/docs/${file}`,
        }
      );
      await emit(join(outDir, pageName(file)), toLf(processed));
      pages++;
    }
  }

  return { pages, title };
}

// How-to guides: docs/howto/*.md → site guides/ section (curated order + readable labels).
const GUIDES_OUT = join(__dirname, "..", "docs", "src", "content", "docs", "guides");
const GUIDES_SRC = join(ROOT, "docs", "howto");
const GUIDE_ORDER = [
  ["showcase-walkthrough.md", "Showcase Walkthrough"],
  ["monorepo-structure.md", "Monorepo Structure"],
  ["authentication-authorization.md", "Authentication & Authorization"],
  ["migrations-concurrent-development.md", "Migrations in Team Development"],
  ["security-hardening.md", "Security Hardening"],
  ["aot-and-trimming.md", "Native AOT & Trimming"],
  ["local-nuget-server.md", "Local NuGet Server"],
  ["consuming-generated-docs.md", "Consuming Generated Docs"],
  ["ide-tips.md", "IDE Tips"],
  ["diagnostics-troubleshooting.md", "Diagnostics & Troubleshooting"],
];

async function syncGuides() {
  if (!existsSync(GUIDES_SRC)) return 0;

  await resetDir(GUIDES_OUT);

  const present = (await readdir(GUIDES_SRC)).filter((f) => f.endsWith(".md"));
  // Curated files first (in GUIDE_ORDER), then any new howto not yet ordered (alphabetical).
  const ordered = [
    ...GUIDE_ORDER.filter(([f]) => present.includes(f)),
    ...present.filter((f) => !GUIDE_ORDER.some(([g]) => g === f)).sort().map((f) => [f, null]),
  ];

  let pages = 0;
  for (let i = 0; i < ordered.length; i++) {
    const [file, label] = ordered[i];
    const content = await readSource(join(GUIDES_SRC, file));
    const title = label || extractTitle(content) || fileSlug(file);
    const description = extractDescription(content);
    // fixLinks with slug "../guides/<name>" so intra-howto links resolve under the guides section.
    const processed = addFrontmatter(
      fixLinks(content, `../guides`, file, `docs/howto/${file}`),
      {
        title, description, order: i + 1,
        editUrl: `${REPO_URL}/edit/main/docs/howto/${file}`,
      }
    );
    await emit(join(GUIDES_OUT, file.toLowerCase()), toLf(processed));
    pages++;
  }
  console.log(`  ✓ docs/howto → guides/ (${pages} pages)`);
  return pages;
}

async function main() {
  console.log("🔄 Syncing module documentation...\n");

  let totalPages = 0;
  const results = [];

  for (const [moduleName, config] of Object.entries(MODULE_MAP)) {
    // Skip source generator (separate section)
    if (config.category === "sg") continue;

    const result = await syncModule(moduleName, config);
    if (result.pages > 0) {
      console.log(`  ✓ ${moduleName} → ${config.slug}/ (${result.pages} pages)`);
      totalPages += result.pages;
      results.push({ module: moduleName, ...config, ...result });
    }
  }

  // How-to guides (docs/howto → guides/)
  const guidePages = await syncGuides();
  totalPages += guidePages;

  if (CHECK) return reportDrift();

  console.log(`\n✅ Synced ${totalPages} pages from ${results.length} modules + ${guidePages} guides.`);
  console.log(`   Output: site/docs/src/content/docs/modules/ + guides/\n`);
  console.log("   Run 'pnpm build:docs' to rebuild the site.");
}

/**
 * Compares what a sync would produce against what is committed, and fails on any difference.
 *
 * Three kinds, all drift: a page whose content changed, one that would be created, and one on disk
 * that nothing generates any more. The third matters as much as the first — a module renamed or a
 * doc deleted leaves a published page that still answers questions about something gone.
 */
async function reportDrift() {
  const onDisk = new Set();
  // Only the sections this script writes. `source-generator/`, `getting-started/` and `reference/`
  // are NOT among them — the sync loop skips `category === "sg"` outright — so this check says
  // nothing about them, and the summary below must not claim otherwise. That gap is real: the site
  // publishes three pages under `source-generator/` whose names match none of the six documents in
  // Pragmatic.SourceGenerator/docs/.
  for (const root of [DOCS_OUT, GUIDES_OUT]) {
    if (!existsSync(root)) continue;
    for (const entry of await readdir(root, { recursive: true, withFileTypes: true })) {
      if (entry.isFile() && entry.name.endsWith(".md"))
        onDisk.add(join(entry.parentPath ?? entry.path, entry.name));
    }
  }

  const changed = [];
  const created = [];
  for (const [path, content] of generated) {
    if (!onDisk.has(path)) { created.push(path); continue; }
    if ((await readFile(path, "utf-8")) !== content) changed.push(path);
  }
  const orphaned = [...onDisk].filter((p) => !generated.has(p));

  const total = changed.length + created.length + orphaned.length;
  if (total === 0) {
    console.log(`✅ modules/ and guides/ are in sync with their sources (${generated.size} pages).`);
    console.log(`   Not covered: source-generator/, getting-started/, reference/ — nothing generates`);
    console.log(`   them, so nothing here can tell you when they go stale.`);
    return;
  }

  const rel = (p) => p.replace(ROOT + sep, "").split(sep).join("/");
  console.error(`\n❌ site/docs has drifted from the module sources — ${total} page(s):\n`);
  for (const p of changed) console.error(`   changed   ${rel(p)}`);
  for (const p of created) console.error(`   missing   ${rel(p)}`);
  for (const p of orphaned) console.error(`   orphaned  ${rel(p)}`);
  console.error(`\n   Fix: node site/scripts/sync-docs.mjs\n`);
  process.exit(1);
}

// Imported by its test for fixLinks alone; run, it syncs.
if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((err) => {
    console.error("❌ Sync failed:", err);
    process.exit(1);
  });
}
