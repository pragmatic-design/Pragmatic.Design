/**
 * When a clean vulnerability verdict may be reused, and what the gate says when it is.
 *
 * ⚠️ The failure this guards is the worst kind a ratchet has: present, green, and answering a
 * question it can no longer answer. The scan is keyed on the dependency graph, and an advisory can be
 * published against a graph that does not move — a transitive dependency every runtime library
 * reaches stays vulnerable, and green, until something unrelated moves the graph.
 *
 * So the verdict has two keys, the graph and the calendar, and this is the calendar. These tests make
 * deleting the age check fail, instead of leaving it one refactor away from the same silence.
 *
 * ⚠️ And the second half, which is not a detail: a reused verdict has to say it is reused. A line
 * that reads the same whether it was scanned or remembered puts the reader back where they started.
 *
 *   node --test scripts/supply-chain.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { SCAN_MAX_AGE_MS, cleanScanLine, scanDecision } from './supply-chain.mjs';

const MINUTE = 60 * 1000;
const NOW = Date.parse('2026-09-09T12:00:00.000Z');
const ago = (ms) => new Date(NOW - ms).toISOString();

const decide = (overrides) => scanDecision({ sameGraph: true, now: NOW, ...overrides });

test('a verdict older than the maximum age is rescanned, on the same graph', () => {
  const decision = decide({ scannedAt: ago(SCAN_MAX_AGE_MS + MINUTE) });

  assert.equal(decision.reuse, false,
    'an unchanged graph is not evidence of an unchanged advisory database');
  assert.match(decision.why, /older/);
});

test('a fresh verdict on the same graph is reused', () => {
  // The control: the cache exists for a reason. Two `dotnet list package` passes over the whole
  // solution take minutes, a large share of the `full` tier. "Scan every time" would be a different
  // way of losing the check, because a gate people stop running answers nothing at all.
  const decision = decide({ scannedAt: ago(30 * MINUTE) });

  assert.equal(decision.reuse, true);
  assert.equal(Math.round(decision.ageMs / MINUTE), 30);
});

test('a graph that moved is rescanned however fresh the verdict', () => {
  const decision = decide({ sameGraph: false, scannedAt: ago(MINUTE) });

  assert.equal(decision.reuse, false,
    'the graph key still decides first: new dependencies have never been scanned');
});

test('a verdict with no usable timestamp is rescanned', () => {
  // A cache written before the age check existed carries no `scannedAt`, and one written by hand or
  // truncated carries something Date.parse cannot read. Both are "unknown age", and unknown age is
  // not young: a NaN comparison is false either way round, so this asserts which way it falls.
  assert.equal(decide({ scannedAt: undefined }).reuse, false);
  assert.equal(decide({ scannedAt: 'whenever' }).reuse, false);
});

test('the line says whether the verdict was scanned or remembered', () => {
  const reused = cleanScanLine(decide({ scannedAt: ago(30 * MINUTE) }));
  const scanned = cleanScanLine(decide({ scannedAt: ago(SCAN_MAX_AGE_MS + MINUTE) }));

  assert.equal(reused.label, 'reused');
  assert.match(reused.detail, /30 min ago/, 'the age of what is being reused');
  assert.match(reused.detail, /12h/, 'and when it stops being reusable');

  assert.equal(scanned.label, 'scanned');
  assert.doesNotMatch(scanned.detail, /reused|ago/,
    'a fresh scan must not read like a remembered one — that is the defect, one level up');
});
