#!/usr/bin/env node
// Every relative link in a package's markdown must resolve inside the package.
//
// A consumer has the package, not the repository: NuGet extracts the .nupkg into the global packages
// folder, and the README a tool or an agent reads there resolves `docs/foo.md` against that folder.
// A link that only resolves in the repository is dead where it is read. Without the docs packed,
// that is every link to `docs/` in every module README — an agent reading the package goes looking
// for the pages the README promises and finds none.
//
//   node scripts/package-markdown-links.mjs <file.nupkg | directory> …
//
// Checks README.md and every other .md in each package. Exit code 1 and one line per dead link when
// any package has one. publish-local runs it on what it has just packed, before pushing: a package
// with a dead link never reaches the feed.

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, posix } from 'node:path';
import { inflateRawSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';

/** The entries of a zip archive, read from its central directory: name → where the data is. */
export function zipEntries(buffer) {
  const minimum = buffer.length - 22 - 0xffff;
  let end = -1;
  for (let i = buffer.length - 22; i >= Math.max(0, minimum); i--) {
    if (buffer.readUInt32LE(i) === 0x06054b50) { end = i; break; }
  }
  if (end < 0) throw new Error('not a zip archive: no end-of-central-directory record');

  const count = buffer.readUInt16LE(end + 10);
  let at = buffer.readUInt32LE(end + 16);
  const entries = new Map();
  for (let n = 0; n < count; n++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) throw new Error(`corrupt central directory at ${at}`);
    const method = buffer.readUInt16LE(at + 10);
    const compressedSize = buffer.readUInt32LE(at + 20);
    const nameLength = buffer.readUInt16LE(at + 28);
    const extraLength = buffer.readUInt16LE(at + 30);
    const commentLength = buffer.readUInt16LE(at + 32);
    const localHeader = buffer.readUInt32LE(at + 42);
    const name = buffer.toString('utf8', at + 46, at + 46 + nameLength);
    entries.set(name, { method, compressedSize, localHeader });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** The bytes of one entry, stored or deflated. */
export function zipRead(buffer, entry) {
  const at = entry.localHeader;
  const start = at + 30 + buffer.readUInt16LE(at + 26) + buffer.readUInt16LE(at + 28);
  const data = buffer.subarray(start, start + entry.compressedSize);
  if (entry.method === 0) return data;
  if (entry.method === 8) return inflateRawSync(data);
  throw new Error(`unsupported zip compression method ${entry.method}`);
}

/**
 * The relative link targets of a markdown document. Code — fenced blocks and inline spans — is
 * blanked first: `](` inside a C# sample is not a link.
 */
export function relativeLinks(markdown) {
  // Only a bare fence closes a block: "```csharp" inside one is content (CommonMark). An unclosed
  // block runs to the end of the document.
  const prose = markdown
    .replace(/^[ \t]*(```|~~~)[^\n]*\n(?:[\s\S]*?^[ \t]*\1+[ \t]*$|[\s\S]*$)/gm, '')
    .replace(/`[^`\n]*`/g, '');
  const targets = [];
  for (const match of prose.matchAll(/\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)/g)) {
    const target = match[1];
    if (/^[a-z][a-z0-9+.-]*:/i.test(target) || target.startsWith('#')) continue;
    targets.push(target);
  }
  return targets;
}

function decoded(text) {
  try { return decodeURIComponent(text); } catch { return text; }
}

/** Whether a link written in the entry `from` lands on a file or folder of the package. */
export function resolvesIn(names, from, target) {
  const path = decoded(target.split('#')[0].split('?')[0]);
  if (path === '') return true;
  if (path.startsWith('/')) return false;

  const normalized = posix.normalize(posix.join(posix.dirname(from), path)).replace(/\/+$/, '');
  if (normalized === '..' || normalized.startsWith('../')) return false;
  if (normalized === '.') return true;

  for (const name of names) {
    if (name === normalized || name.startsWith(`${normalized}/`)) return true;
  }
  return false;
}

/** The links of one package's markdown that resolve to nothing inside it, as `entry: target`. */
export function deadLinks(nupkg) {
  const buffer = readFileSync(nupkg);
  const entries = zipEntries(buffer);
  const names = [...entries.keys()].map(decoded);
  const dead = [];
  for (const [name, entry] of entries) {
    if (!name.toLowerCase().endsWith('.md')) continue;
    const from = decoded(name);
    for (const target of relativeLinks(zipRead(buffer, entry).toString('utf8'))) {
      if (!resolvesIn(names, from, target)) dead.push(`${from}: ${target}`);
    }
  }
  return dead;
}

function packagesIn(paths) {
  const found = [];
  for (const path of paths) {
    if (statSync(path).isDirectory()) {
      for (const name of readdirSync(path))
        if (name.endsWith('.nupkg') && !name.endsWith('.symbols.nupkg')) found.push(join(path, name));
    } else {
      found.push(path);
    }
  }
  return found;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const packages = packagesIn(process.argv.slice(2));
  if (packages.length === 0) {
    console.error('no .nupkg given');
    process.exit(2);
  }

  let dead = 0;
  let affected = 0;
  for (const nupkg of packages) {
    const links = deadLinks(nupkg);
    if (links.length === 0) continue;
    affected++;
    dead += links.length;
    for (const link of links) console.log(`${posix.basename(nupkg.replaceAll('\\', '/'))} ${link}`);
  }

  console.log(`${packages.length} package(s), ${affected} with dead links, ${dead} dead link(s)`);
  process.exit(dead === 0 ? 0 : 1);
}
