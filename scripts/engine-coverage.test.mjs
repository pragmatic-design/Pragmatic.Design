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

test('kind 2 carries the owner\'s decision, not an open question', () => {
  const page = expectedPage(root);
  const kindTwo = page.slice(page.indexOf('## 2.'), page.indexOf('## 3.'));
  assert.ok(!kindTwo.includes('**Open:**'), 'section 2 still says the choice is open');
  assert.ok(kindTwo.includes('2026-11-22'), 'section 2 does not date the owner\'s decision');
  for (const issue of ['#946', '#947']) assert.ok(kindTwo.includes(issue), `section 2 does not name ${issue}, the grid on the engines`);
});

test('kind 4 lists a gap no suite test exercises, with its issue', () => {
  const page = expectedPage(root);
  const kindFour = page.slice(page.indexOf('## 4.'), page.indexOf('## 5.'));
  assert.ok(!kindFour.includes('None found yet'), 'section 4 says no gap was found');
  assert.ok(kindFour.includes('`FullJoin`') && kindFour.includes('#962'), 'section 4 does not name the .NET 11 operators and their issue');
});

test('kind 4 no longer lists the outer joins as missing on the Sql engine, and names what is still missing', () => {
  const page = expectedPage(root);
  const kindFour = page.slice(page.indexOf('## 4.'), page.indexOf('## 5.'));
  assert.ok(!kindFour.includes('neither engine'), 'section 4 still says neither engine has the operators');
  assert.ok(kindFour.includes('The Sql engine compiles the three joins'), 'section 4 does not say the Sql engine compiles the outer joins');
  for (const issue of ['#969', '#970', '#971']) assert.ok(kindFour.includes(issue), `section 4 does not name ${issue}, a part still missing`);
});

test('a test added to a file left out moves the page: the count is read from the source, not written on it', () => {
  // A file the page counts the methods of: one EF Core only, which stays left out whatever kind 1 holds.
  const [leftOut] = Object.entries(ALLOWED_FILE_EXCLUSIONS).find(([, a]) => a.reason === SUITE_REASONS.EF_ONLY);
  const one = (path) => (path === leftOut ? '[Fact] public void A() {}' : null);
  const two = (path) => (path === leftOut ? '[Fact] public void A() {}\n[Fact] public void B() {}' : null);
  assert.notEqual(coveragePage(one, []), coveragePage(two, []));
});
