#!/usr/bin/env node
/**
 * Copies the examples each skill ships from the code they are taken from.
 *
 * A skill is read outside this repository, so its examples have to live in its own folder — and an
 * example written by hand ages like the prose around it: nothing compiles it and nothing runs it. These
 * are copies of files that compile and are tested (scripts/skill-examples.json says which, and which
 * suite covers them), byte for byte, so the only way an example changes is its source changing. The one
 * rewrite is in a project file, whose references into the framework become packages (asConsumerProject).
 *
 *   node scripts/sync-skill-examples.mjs           writes the copies and each examples/README.md
 *   node scripts/sync-skill-examples.mjs --check   exits 1 when a copy differs from its source, a
 *                                                  source is gone, or the folder holds a file the
 *                                                  manifest does not name
 *   --root <dir>                                   another tree with the same layout (the tests)
 */
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const rootArg = process.argv.indexOf('--root');
const root = rootArg > 0
  ? resolve(process.argv[rootArg + 1])
  : join(dirname(fileURLToPath(import.meta.url)), '..');
const skillsDir = join(root, 'marketplace', 'plugins', 'pragmatic-design', 'skills');
const manifest = JSON.parse(readFileSync(join(root, 'scripts', 'skill-examples.json'), 'utf8'));
const check = process.argv.includes('--check');

const drift = [];

for (const [skill, entry] of Object.entries(manifest.skills)) {
  const target = join(skillsDir, skill, 'examples');
  const expected = new Map();

  for (const [file, description] of Object.entries(entry.files)) {
    const from = join(root, entry.source, file);
    if (!existsSync(from)) {
      drift.push(`${skill}: source missing — ${entry.source}/${file}`);
      continue;
    }
    const bytes = readFileSync(from);
    expected.set(file, {
      bytes: file.endsWith('.csproj') ? Buffer.from(asConsumerProject(bytes.toString('utf8')), 'utf8') : bytes,
      description,
    });
  }

  expected.set('README.md', { bytes: Buffer.from(readme(skill, entry), 'utf8') });

  for (const [file, { bytes }] of expected) {
    const to = join(target, file);
    const current = existsSync(to) ? readFileSync(to) : null;
    if (current !== null && sameText(current, bytes)) continue;

    if (check) {
      drift.push(`${skill}: ${current === null ? 'missing' : 'differs from its source'} — examples/${file}`);
    } else {
      mkdirSync(dirname(to), { recursive: true });
      writeFileSync(to, bytes);
    }
  }

  // A file the manifest no longer names is a copy nothing keeps current.
  for (const file of filesUnder(target)) {
    if (expected.has(file)) continue;
    if (check) drift.push(`${skill}: not in the manifest — examples/${file}`);
    else rmSync(join(target, file));
  }
}

if (check) {
  if (drift.length > 0) {
    console.log(`skill examples out of sync — ${drift.length}:`);
    for (const line of drift) console.log(`  ${line}`);
    console.log('  Fix: node scripts/sync-skill-examples.mjs');
    process.exit(1);
  }
  const count = Object.values(manifest.skills).reduce((n, s) => n + Object.keys(s.files).length, 0);
  console.log(`skill examples in sync (${count} files in ${Object.keys(manifest.skills).length} skills)`);
} else if (drift.length > 0) {
  for (const line of drift) console.error(line);
  process.exit(1);
}

/**
 * A project file as an application that consumes the packages writes it.
 *
 * In the repository an example application references the framework by path, into the framework's own
 * projects; a reader of the skill has no such path, and every skill tells them to use the packages. So
 * the copy rewrites what only the repository has: a reference to a `Pragmatic.*` project becomes the
 * package of the same name (an analyzer one private to the project), and an `Import` of a framework
 * `.targets` goes, because the package brings its own. The application's references to its own projects
 * stay as they are.
 */
export function asConsumerProject(text) {
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  let imported = false;
  return text
    .replace(/<ProjectReference\s+Include="([^"]*)"([^>]*?)\/>/gs, (element, include, rest) => {
      const id = /(?:^|[\\/])(Pragmatic\.[^\\/]+)\.csproj$/.exec(include)?.[1];
      if (!id) return element;
      return /OutputItemType="Analyzer"/.test(rest)
        ? `<PackageReference Include="${id}" PrivateAssets="all" />`
        : `<PackageReference Include="${id}" />`;
    })
    .replace(/^([ \t]*)<Import\s+Project="[^"]*[\\/]Pragmatic\.[^"]*"\s*\/>[ \t]*\r?\n/gm, (_, indent) => {
      if (imported) return '';
      imported = true;
      return `${indent}<!-- The framework's build targets come with its packages: nothing to import. -->${eol}`;
    });
}

// Git rewrites line endings on checkout (core.autocrlf), and not necessarily the same way for a source
// and for the README this script generates: two files that differ only there are the same example.
function sameText(a, b) {
  const lf = (buffer) => buffer.toString('utf8').replace(/\r\n/g, '\n');
  return a.equals(b) || lf(a) === lf(b);
}

function filesUnder(dir) {
  if (!existsSync(dir)) return [];
  const out = [];
  const walk = (d) => {
    for (const name of readdirSync(d)) {
      const p = join(d, name);
      if (statSync(p).isDirectory()) walk(p);
      else out.push(relative(dir, p).split('\\').join('/'));
    }
  };
  walk(dir);
  return out;
}

function readme(skill, entry) {
  // One suite or several: an example taken from two applications is exercised by both.
  const suites = [entry.testedBy].flat().map((suite) => `\`${suite}\``);
  const testedBy = suites.length > 1 ? `${suites.slice(0, -1).join(', ')} and ${suites.at(-1)}` : suites[0];
  const lines = [
    `# Examples: ${skill}`,
    '',
    `Copied from \`${entry.source}\`, which compiles in the repository and is exercised by`,
    `${testedBy}. **Do not edit here**: change the source and run`,
    '`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.',
    '',
  ];
  if (Object.keys(entry.files).some((file) => file.endsWith('.csproj')))
    lines.push(
      'A project file is the one rewrite: its references to the framework\'s own projects are written as',
      'the packages of the same name, as an application that consumes them references them.',
      '');
  lines.push(
    '| File | What it shows |',
    '|---|---|',
  );
  for (const [file, description] of Object.entries(entry.files))
    lines.push(`| [\`${file}\`](${file}) | ${description} |`);
  lines.push('');
  return lines.join('\n');
}
