// node --test scripts/engine-coverage.test.mjs
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { ALLOWED_EXCLUSIONS } from './engine-projects.mjs';
import { ALLOWED_FILE_EXCLUSIONS, ALLOWED_TEST_EXCLUSIONS, ENGINE_SUITES, SUITE_DIR, SUITE_REASONS, engineOf, suiteCount } from './engine-suites.mjs';
import { coveragePage, expectedPage, suiteSources } from './engine-coverage.mjs';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const read = (path) => (existsSync(join(root, path)) ? readFileSync(join(root, path), 'utf-8') : null);
const inSuite = (path) => path.slice(SUITE_DIR.length + 1);

test('the page names every exclusion of both lists, so it cannot leave one out', () => {
  const page = expectedPage(root);
  for (const path of Object.keys(ALLOWED_EXCLUSIONS)) assert.ok(page.includes(path), `module exclusion ${path} is not on the page`);
  for (const path of Object.keys(ALLOWED_FILE_EXCLUSIONS)) assert.ok(page.includes(inSuite(path)), `suite file ${path} is not on the page`);
  for (const a of Object.values(ALLOWED_TEST_EXCLUSIONS)) assert.ok(page.includes(inSuite(a.file)), `${a.file}, with a test left out, is not on the page`);
});

test('the counts on the page are the ones the gate reports', () => {
  const page = expectedPage(root);
  for (const project of ENGINE_SUITES) {
    const c = suiteCount(project, suiteSources(root), read);
    assert.ok(page.includes(`| ${engineOf(project)} | **${c.runs}** | ${c.all} | ${c.files} | ${c.filtered} |`), `the ${engineOf(project)} row is not ${c.runs} of ${c.all}`);
  }
});

test('a test added to a file left out moves the page: the count is read from the source, not written on it', () => {
  const [onEf] = Object.entries(ALLOWED_FILE_EXCLUSIONS).find(([, a]) => a.reason === SUITE_REASONS.TEST_ON_EF);
  const one = (path) => (path === onEf ? '[Fact] public void A() {}' : null);
  const two = (path) => (path === onEf ? '[Fact] public void A() {}\n[Fact] public void B() {}' : null);
  assert.notEqual(coveragePage(one, []), coveragePage(two, []));
});
