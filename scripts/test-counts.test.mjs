/**
 * The gate counts every summary `dotnet test` prints: one per test assembly, two for a project that targets
 * two frameworks (#832).
 *
 *   node --test scripts/test-counts.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { summedCount } from './lib/test-counts.mjs';

// The detailed logger's block, as the gate runs it, once per target framework.
const block = (passed, failed) => `
Esecuzione dei test per Pragmatic.Integration.Tests.dll
Totale test: ${passed + failed}
     Superati: ${passed}
     Non superati: ${failed}
 Tempo totale: 1,2 Secondi`;

test('a red second target is counted, after a green first one', () => {
  const out = block(56, 0) + block(52, 4);

  assert.equal(summedCount(out, 'Non superati|Failed', true), 4);
  assert.equal(summedCount(out, 'Superati|Passed', true), 108);
});

test('one target counts as it did', () => {
  assert.equal(summedCount(block(15, 0), 'Superati|Passed', true), 15);
  assert.equal(summedCount(block(15, 0), 'Non superati|Failed', true), 0);
});

test('the default logger\'s one-line shape is summed too', () => {
  const out = [
    'Superato!     - Non superati:     0. Superati:    56. Ignorati:     0. Totale:    56. - X.dll (net11.0)',
    'Non superato! - Non superati:     2. Superati:    54. Ignorati:     0. Totale:    56. - X.dll (net10.0)',
  ].join('\n');

  assert.equal(summedCount(out, 'Non superati|Failed', false), 2);
  assert.equal(summedCount(out, 'Superati|Passed', false), 110);
});

test('the block reading does not take the one-line shape\'s mid-line count', () => {
  const out = 'Superato!     - Non superati:     3. Superati:    56.';
  assert.equal(summedCount(out, 'Non superati|Failed', true), 0);
});
