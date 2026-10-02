/**
 * The package link check, on archives built here.
 *
 * ⚠️ What this file does and does not cover. It proves the checker reads a .nupkg and judges its links
 * the way a consumer's folder would resolve them. It does not pack anything: whether the pack step
 * ships the docs and rewrites the links is proven where packages are made — publish-local runs the
 * checker on every package it is about to push, and refuses to push on a dead link.
 *
 *   node --test scripts/package-markdown-links.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { deflateRawSync } from 'node:zlib';
import { deadLinks, relativeLinks } from './package-markdown-links.mjs';

/** A zip with one deflated entry per file: the parts of the format the checker reads, no more. */
function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text] of Object.entries(files)) {
    const nameBytes = Buffer.from(name, 'utf8');
    const data = deflateRawSync(Buffer.from(text, 'utf8'));
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(8, 8);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(8, 10);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(Object.keys(files).length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

function nupkg(files) {
  const path = join(mkdtempSync(join(tmpdir(), 'pkg-links-')), 'Sample.1.0.0.nupkg');
  writeFileSync(path, zip(files));
  return path;
}

test('a link to a page that ships resolves, and one to a page that does not is named', () => {
  const path = nupkg({
    'README.md': '[Concepts](docs/concepts.md) and [Gone](docs/gone.md#anchor)',
    'docs/concepts.md': '# Concepts',
  });
  assert.deepEqual(deadLinks(path), ['README.md: docs/gone.md#anchor']);
});

test('a link resolves against the folder of the page that writes it', () => {
  const path = nupkg({
    'README.md': '# Readme',
    'docs/how-it-works/a.md': '[up](../../README.md) [sibling](b.md) [out](../../../Other/README.md)',
    'docs/how-it-works/b.md': '# B',
  });
  assert.deepEqual(deadLinks(path), ['docs/how-it-works/a.md: ../../../Other/README.md']);
});

test('a link to a folder of the package resolves', () => {
  const path = nupkg({ 'README.md': '[all the docs](docs/)', 'docs/a.md': '# A' });
  assert.deepEqual(deadLinks(path), []);
});

test('an absolute URL, a mail link and an anchor are not relative links', () => {
  assert.deepEqual(
    relativeLinks('[a](https://example.com/x.md) [b](mailto:x@y.z) [c](#section) [d](docs/d.md)'),
    ['docs/d.md']);
});

test('code is not prose: a link inside a fence or an inline span is not a link', () => {
  const markdown = [
    'Before `handlers[0](args)` inline.',
    '```csharp',
    'var x = list[0](value);',
    '```',
    '[real](docs/real.md)',
  ].join('\n');
  assert.deepEqual(relativeLinks(markdown), ['docs/real.md']);
});

test('only a bare fence closes a block: an info string inside one is content', () => {
  // The shape found in Pragmatic.Specification/docs/common-mistakes.md: a block never closed before
  // the next "```csharp". Read as a closer, every block after it flips inside out.
  const markdown = [
    '```csharp',
    'first();',
    '```csharp',
    'second(list[0](x));',
    '```',
    '[after](docs/after.md)',
  ].join('\n');
  assert.deepEqual(relativeLinks(markdown), ['docs/after.md']);
});

test('a block left open runs to the end of the document', () => {
  assert.deepEqual(relativeLinks('[before](a.md)\n```\n[inside](b.md)\n'), ['a.md']);
});
