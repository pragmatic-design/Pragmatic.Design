/**
 * Links that leave the site: a relative link a page cannot serve becomes a GitHub URL, and that URL
 * must name the file the source document pointed at — resolved from where the document lives, not
 * from the repository root.
 *
 *   node --test site/scripts/sync-docs.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { extractTitle, fixLinks, normalizeSource } from './sync-docs.mjs';

const REPO = 'https://github.com/pragmatic-design/Pragmatic.Design';

test('a sample linked from a module doc resolves inside that module', () => {
  const out = fixLinks('[csv](../samples/Pragmatic.Documents.Csv.Samples/README.md)',
    'documents', 'csv-io.md', 'Pragmatic.Documents/docs/csv-io.md');

  assert.equal(out, `[csv](${REPO}/blob/main/Pragmatic.Documents/samples/Pragmatic.Documents.Csv.Samples/README.md)`);
});

test('a bare relative link in a README resolves inside the module', () => {
  const out = fixLinks('See [results](BENCHMARK-RESULTS.md) and [samples](samples/).',
    'logging', '', 'Pragmatic.Logging/README.md');

  assert.equal(out,
    `See [results](${REPO}/blob/main/Pragmatic.Logging/BENCHMARK-RESULTS.md) and [samples](${REPO}/tree/main/Pragmatic.Logging/samples).`);
});

test('a guide links to a repository document from docs/howto', () => {
  const out = fixLinks('[diagnostics](../diagnostics.md)', '../guides', '', 'docs/howto/x.md');

  assert.equal(out, `[diagnostics](${REPO}/blob/main/docs/diagnostics.md)`);
});

test('a sibling doc written bare becomes its page on the site', () => {
  const out = fixLinks('[Concepts](concepts.md) and [checklist](concepts.md#checklist)',
    'abstractions', 'getting-started.md', 'Pragmatic.Abstractions/docs/getting-started.md');

  assert.equal(out,
    '[Concepts](/modules/abstractions/concepts/) and [checklist](/modules/abstractions/concepts/#checklist)');
});

test('a sibling guide written bare becomes its guide page', () => {
  const out = fixLinks('[IDE](ide-tips.md)', '../guides', 'x.md', 'docs/howto/x.md');

  assert.equal(out, '[IDE](/guides/ide-tips/)');
});

// A source saved with a byte-order mark has it before the `#`: the H1 was not found on its line, and
// the page was titled with its file name — "02-entity-system" — on fourteen pages.
test('a source saved with a byte-order mark keeps its H1 as the title', () => {
  const source = normalizeSource('﻿# Entity System\r\n\r\nThis guide explains.\r\n');

  assert.equal(extractTitle(source), 'Entity System');
});

test('the control: a source without a byte-order mark reads the same', () => {
  assert.equal(normalizeSource('# Entity System\r\n'), '# Entity System\n');
});

test('the control: anchors, absolute links and site pages are left alone', () => {
  const out = fixLinks('[a](#anchor) [w](https://example.com) [p](/modules/x/) [m](mailto:a@b.c)',
    'logging', 'concepts.md', 'Pragmatic.Logging/docs/concepts.md');

  assert.equal(out, '[a](#anchor) [w](https://example.com) [p](/modules/x/) [m](mailto:a@b.c)');
});
