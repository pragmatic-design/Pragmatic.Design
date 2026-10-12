#!/usr/bin/env node
/**
 * skills-version — the agent skills name one package version, and move to a new one in one command (#218).
 *
 * The skills under marketplace/ reference the packages they teach by version: `PackageReference` lines, a
 * `<PragmaticVersion>`, and the version named in prose. After a release they all move together, and the
 * plugin's own version moves with them, in plugin.json and marketplace.json at once: an installed copy of
 * the skills updates only when that number changes.
 *
 *   node scripts/skills-version.mjs check            exit 1 when the skills name more than one package
 *                                                    version, or the two manifests disagree (the full tier)
 *   node scripts/skills-version.mjs set <version>    after `nuget-v<version>` is tagged: every occurrence of
 *                                                    the current version under marketplace/ becomes <version>,
 *                                                    and the plugin's patch version goes up in both manifests
 *
 * `set` refuses a version with no `nuget-v` tag in the local repository, so the skills never point an agent at
 * packages that were not released. The tags are local knowledge: CI checks out without them, which is why
 * `check` does not compare against them.
 */
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const MARKETPLACE = 'marketplace';

export const MANIFESTS = {
  plugin: 'marketplace/plugins/pragmatic-design/.claude-plugin/plugin.json',
  marketplace: 'marketplace/.claude-plugin/marketplace.json',
};

/** Where the skills name a package version: a Pragmatic package reference, or the version property. */
const VERSION_SITES = [
  /Include="Pragmatic\.[\w.]+"\s+Version="([^"]+)"/g,
  /<PragmaticVersion>([^<]+)<\/PragmaticVersion>/g,
];

const MANIFEST_VERSION = /("version":\s*")([^"]+)(")/;

/** Each package version the skills name, with the files that name it. */
export function packageVersions(files) {
  const versions = new Map();
  for (const [path, text] of files) {
    if (Object.values(MANIFESTS).includes(path)) continue;
    for (const site of VERSION_SITES) {
      for (const m of text.matchAll(site)) {
        // `$(PragmaticVersion)` refers to the version set once in the scaffold; it is not a second one.
        if (m[1].startsWith('$(')) continue;
        const paths = versions.get(m[1]) ?? new Set();
        paths.add(path);
        versions.set(m[1], paths);
      }
    }
  }
  return versions;
}

function manifestVersion(files, key) {
  return MANIFEST_VERSION.exec(files.get(MANIFESTS[key]) ?? '')?.[2] ?? null;
}

/** What is wrong with the skills' versions: empty when they name one package version and the manifests agree. */
export function versionProblems(files) {
  const problems = [];

  const versions = packageVersions(files);
  if (versions.size > 1) {
    const lines = [...versions].map(([v, paths]) => `    ${v}: ${[...paths].sort().join(', ')}`);
    problems.push(`the skills name more than one package version:\n${lines.join('\n')}`);
  }

  const plugin = manifestVersion(files, 'plugin');
  const marketplace = manifestVersion(files, 'marketplace');
  if (plugin !== marketplace)
    problems.push(`the plugin version differs between ${MANIFESTS.plugin} (${plugin}) and ${MANIFESTS.marketplace} (${marketplace})`);

  return problems;
}

/** `1.2.3` → `1.2.4`. */
export function nextPatch(version) {
  const m = /^(\d+)\.(\d+)\.(\d+)$/.exec(version);
  if (!m) throw new Error(`the plugin version '${version}' is not major.minor.patch`);
  return `${m[1]}.${m[2]}.${Number(m[3]) + 1}`;
}

/**
 * The files `set` rewrites, as a map of path to new text, with the version moved from and the plugin version
 * moved. Throws when the version was never tagged, or when the skills do not start from one version.
 */
export function setVersion(files, version, { tagExists }) {
  if (!tagExists(`nuget-v${version}`))
    throw new Error(`no tag nuget-v${version}: release the packages first, then move the skills to them`);

  const problems = versionProblems(files);
  if (problems.length > 0) throw new Error(`the skills must name one version before they move:\n${problems.join('\n')}`);

  const versions = [...packageVersions(files).keys()];
  if (versions.length === 0) throw new Error('the skills name no package version');
  const [from] = versions;

  const changed = new Map();
  if (from !== version) {
    for (const [path, text] of files) {
      if (Object.values(MANIFESTS).includes(path) || !text.includes(from)) continue;
      changed.set(path, text.replaceAll(from, version));
    }
  }

  const pluginFrom = manifestVersion(files, 'plugin');
  const pluginTo = nextPatch(pluginFrom);
  for (const key of Object.keys(MANIFESTS))
    changed.set(MANIFESTS[key], files.get(MANIFESTS[key]).replace(MANIFEST_VERSION, `$1${pluginTo}$3`));

  return { changed, from, plugin: { from: pluginFrom, to: pluginTo } };
}

/** Every text file under marketplace/, by path relative to the repository root ('/'-separated). */
function readMarketplace(root) {
  const files = new Map();
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      const full = join(dir, name);
      if (statSync(full).isDirectory()) walk(full);
      else if (/\.(md|json)$/.test(name)) files.set(relative(root, full).replaceAll('\\', '/'), readFileSync(full, 'utf8'));
    }
  };
  walk(join(root, MARKETPLACE));
  return files;
}

function tagExists(tag) {
  return execFileSync('git', ['-C', ROOT, 'tag', '-l', tag], { encoding: 'utf8' }).trim() === tag;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [command, version] = process.argv.slice(2);
  const files = readMarketplace(ROOT);

  if (command === 'check') {
    const problems = versionProblems(files);
    if (problems.length === 0) {
      const [only] = packageVersions(files).keys();
      console.log(`skills name one package version (${only ?? 'none'}), plugin ${manifestVersion(files, 'plugin')} in both manifests`);
      process.exit(0);
    }
    for (const p of problems) console.log(`  ${p}`);
    process.exit(1);
  }

  if (command === 'set' && version) {
    try {
      const { changed, from, plugin } = setVersion(files, version, { tagExists });
      for (const [path, text] of changed) writeFileSync(join(ROOT, path), text);
      console.log(`${from} → ${version} in ${changed.size - 2} file(s); plugin ${plugin.from} → ${plugin.to} in both manifests`);
      process.exit(0);
    } catch (e) {
      console.error(e.message);
      process.exit(1);
    }
  }

  console.error('usage: node scripts/skills-version.mjs check | set <version>');
  process.exit(2);
}
