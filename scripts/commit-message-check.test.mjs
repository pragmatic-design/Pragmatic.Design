// Cases for commit-message-check.mjs: each rule refuses what a public history must not carry, and a
// well-formed message passes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { messageProblems } from './commit-message-check.mjs';

test('the control: a conventional message with a body passes', () => {
  assert.deepEqual(messageProblems('fix(persistence): a soft-deleted row stays out of a count\n\nThe filter was applied after the aggregate.\n'), []);
});

test('a breaking change marker and a scope-less header pass', () => {
  assert.deepEqual(messageProblems('refactor!: rename the host builder'), []);
  assert.deepEqual(messageProblems('docs: say what ships'), []);
});

test('a header without a conventional type is refused', () => {
  assert.match(messageProblems('Fixed the bug').join('\n'), /type\(scope\): description/);
  assert.match(messageProblems('feature(x): add it').join('\n'), /type\(scope\): description/);
});

test('a header over 72 characters is refused', () => {
  assert.match(messageProblems(`feat: ${'x'.repeat(67)}`).join('\n'), /73 characters/);
  assert.deepEqual(messageProblems(`feat: ${'x'.repeat(66)}`), []);
});

test('a description starting with a capital letter is refused', () => {
  assert.match(messageProblems('fix: Handle the null case').join('\n'), /lowercase/);
});

test('a tracker key anywhere in the message is refused', () => {
  assert.match(messageProblems('fix: a count\n\nCloses PRAG-123.').join('\n'), /tracker key/);
});

test('a Claude-Session trailer is refused', () => {
  assert.match(messageProblems('fix: a count\n\nClaude-Session: https://claude.ai/code/session_x').join('\n'), /session link/);
});

test('a path from a maintainer machine is refused', () => {
  assert.match(messageProblems('fix: a count\n\nSeen in C:\\Users\\someone\\repo.').join('\n'), /local path/);
});

test('git-generated messages pass: reverts, fixups, squashes', () => {
  assert.deepEqual(messageProblems('Revert "fix: a count"\n\nThis reverts commit abc.'), []);
  assert.deepEqual(messageProblems('fixup! fix: a count'), []);
  assert.deepEqual(messageProblems('squash! fix: a count'), []);
});

test('comment lines git strips are not checked', () => {
  assert.deepEqual(messageProblems('fix: a count\n# Please enter the commit message. PRAG-1\n'), []);
});
