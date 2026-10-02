/**
 * The path somebody else arrives by.
 *
 * ⚠️ Every one of these held a wrong answer at once. `publish-local.mjs` read
 * `C:/Pragmatic/bagetter.nuget.config` — a file on the author's machine, outside the repository,
 * created by nothing. The consumer samples asked for `0.1.0-preview.*` while the script published
 * `1.0.0-alpha.0.*`, so they restored cleanly from nuget.org and proved nothing about the packages
 * they exist to prove. And the how-to described a `NuGet.Config` at the repo root that is not there.
 *
 * What is checked here is what can regress silently. A clean clone actually working is a walk, and
 * the walk is in the issue.
 *
 *   node --test scripts/first-run.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const PUBLISH = 'scripts/publish-local.mjs';
const SAMPLES = 'examples/consumer-samples';

/** The file with comments blanked, positions preserved — a path named in a remark is not a path used. */
function code(source) {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, (m) => ' '.repeat(m.length))
    .replace(/(^|\n)([^\n]*?)\/\/[^\n]*/g, (m, head, before) =>
      head + before + ' '.repeat(m.length - head.length - before.length));
}

/** The version prefix the publish script actually uses. */
function publishedPrefix() {
  const source = code(readFileSync(PUBLISH, 'utf-8'));
  const match = /const PREFIX = '([^']+)'/.exec(source);
  assert.ok(match, `${PUBLISH} must declare the version prefix it publishes`);
  return match[1];
}

test('the publish script names no path outside the repository', () => {
  const source = code(readFileSync(PUBLISH, 'utf-8'));

  const absolute = source.match(/'[A-Za-z]:[\\/][^']*'/g) ?? [];

  assert.deepEqual(absolute, [],
    `a path only the author's machine has is the first thing a new contributor trips on: ${absolute}`);
});

test('the consumer samples ask for the version the script publishes', () => {
  const prefix = publishedPrefix();

  const projects = readdirSync(SAMPLES, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => join(SAMPLES, e.name, `${e.name}.csproj`))
    .filter(existsSync);

  assert.ok(projects.length > 0, 'the samples are the reason the feed exists — finding none is the bug');

  for (const project of projects) {
    const xml = readFileSync(project, 'utf-8');
    const pragmatic = [...xml.matchAll(/Include="(Pragmatic\.[^"]+)"\s+Version="([^"]+)"/g)];

    assert.ok(pragmatic.length > 0, `${project} references no Pragmatic package`);

    for (const [, pkg, version] of pragmatic) {
      // A bare "*" takes whatever the feed holds, which is what the source generator reference does
      // on purpose; a pinned floating version has to be the one being published.
      if (version === '*') continue;

      assert.ok(version.startsWith(prefix),
        `${project} asks ${pkg} for ${version}, and the script publishes ${prefix}N — `
        + 'a sample on a version nobody publishes restores from nuget.org and proves nothing');
    }
  }
});

test('the how-to does not promise a NuGet.Config at the root', () => {
  const guide = readFileSync('docs/howto/local-nuget-server.md', 'utf-8');
  const rootConfigExists = existsSync('NuGet.Config') || existsSync('nuget.config');

  assert.equal(rootConfigExists, false,
    'if one is ever added, this case is the wrong record and the guide has to change with it');

  assert.ok(!/`NuGet\.Config` at the repo root \(the file already exists/.test(guide),
    'the guide told the reader to edit a file that is not there');
});

test('the samples carry the feed they are meant to read', () => {
  const config = join(SAMPLES, 'NuGet.config');

  assert.ok(existsSync(config), `${config} is what makes the samples read the local feed`);

  const xml = readFileSync(config, 'utf-8');
  assert.match(xml, /localhost:5555/, 'the local BaGetter');
  assert.match(xml, /packageSourceMapping/,
    'without the mapping a Pragmatic package can come from nuget.org and the shakedown is a fiction');
});
