/**
 * The check that keeps the Showcase's integration suite on each engine host to its listed exclusions, and the filter it runs with.
 *
 *   node --test scripts/engine-suites.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ALLOWED_FILE_EXCLUSIONS, ALLOWED_TEST_EXCLUSIONS, SUITE_DIR, engineSuiteProblems, suiteCount, suiteFilter, testMethods } from './engine-suites.mjs';

const SQL = 'examples/showcase/tests/Showcase.Sql.IntegrationTests/Showcase.Sql.IntegrationTests.csproj';
const remove = (file) => `<Compile Remove="..\\Showcase.IntegrationTests\\${file.slice(SUITE_DIR.length + 1).replaceAll('/', '\\')}" />`;
const allowedOn = (engine) => Object.entries(ALLOWED_FILE_EXCLUSIONS).filter(([, a]) => a.engines.includes(engine)).map(([file]) => file);

/** The Sql suite as the list says it should be: every file allowed out on Sql, removed under a comment. */
const sqlSuite = (extra = '', skip = null) => ({
  path: SQL,
  text: `<Project>\n  <!-- why -->\n  <ItemGroup>\n${allowedOn('Sql').filter((f) => f !== skip).map(remove).join('\n')}\n${extra}\n  </ItemGroup>\n</Project>`,
});
const read = (path) => (path.startsWith(SUITE_DIR) ? '[Fact]\npublic async Task Something_Holds()\n{\n}' : null);

test('the Sql suite leaving out exactly its listed files, under a comment: nothing to report', () => {
  assert.deepEqual(engineSuiteProblems([sqlSuite()], read), []);
});

test('a file left out that is not on the list is reported: a test that fails there is an issue, not an exclusion', () => {
  const problems = engineSuiteProblems([sqlSuite(remove(`${SUITE_DIR}/Endpoints/CommentEndpointTests.cs`))], read);
  assert.match(problems[0], /CommentEndpointTests\.cs, which is not an allowed exclusion on the Sql engine/);
});

test('an exclusion allowed on the documents engine only is reported on the Sql one', () => {
  const problems = engineSuiteProblems([sqlSuite(remove(`${SUITE_DIR}/Migrations/MigrationPipelineTests.cs`))], read);
  assert.match(problems[0], /MigrationPipelineTests\.cs, which is not an allowed exclusion on the Sql engine/);
});

test('an allowed exclusion the project does not take is reported: the list has gone stale', () => {
  const problems = engineSuiteProblems([sqlSuite('', `${SUITE_DIR}/Endpoints/GridSearchTests.cs`)], read);
  assert.deepEqual(problems, [`${SUITE_DIR}/Endpoints/GridSearchTests.cs is allowed out of the Sql suite and ${SQL} compiles it: take it off the list, or out of the project`]);
});

test('an exclusion with no comment saying why is reported', () => {
  const suite = sqlSuite();
  const bare = { ...suite, text: suite.text.replace('<!-- why -->', '') };
  assert.match(engineSuiteProblems([bare], read)[0], /with no comment saying why/);
});

test('an exclusion of a file that does not exist is reported', () => {
  assert.match(engineSuiteProblems([sqlSuite()], () => null)[0], /which does not exist/);
});

test('a project not named for its engine is reported', () => {
  assert.deepEqual(engineSuiteProblems([{ path: 'a/Showcase.Oracle.IntegrationTests.csproj', text: '<Project />' }], read),
    ['a/Showcase.Oracle.IntegrationTests.csproj: an engine suite is named for its engine (Showcase.Sql.IntegrationTests or Showcase.Documents.IntegrationTests)']);
});

test('every exclusion that follows from a module file names one the module projects leave out on its engines', () => {
  assert.deepEqual(engineSuiteProblems([], read), []);
});

test('a test excluded by name that its file no longer declares is reported, and the filter leaves out what is listed', () => {
  const name = 'Showcase.IntegrationTests.Endpoints.SomeTests.Gone';
  ALLOWED_TEST_EXCLUSIONS[name] = { file: `${SUITE_DIR}/Endpoints/SomeTests.cs`, engines: ['Sql'], reason: 'test' };
  try {
    assert.deepEqual(engineSuiteProblems([sqlSuite()], read), [`${name} is left out of the Sql suite and ${SUITE_DIR}/Endpoints/SomeTests.cs declares no such test`]);
    assert.deepEqual(suiteFilter(SQL), ['--filter', `FullyQualifiedName!=${name}`]);
    assert.deepEqual(suiteFilter('examples/showcase/tests/Showcase.IntegrationTests/Showcase.IntegrationTests.csproj'), []);
  } finally {
    delete ALLOWED_TEST_EXCLUSIONS[name];
  }
});

test('the test methods of a source are its Fact and Theory methods', () => {
  const source = '[Fact]\npublic async Task A()\n{}\n[Theory]\n[InlineData(1)]\npublic void B(int x)\n{}\npublic void NotATest()\n{}';
  assert.deepEqual(testMethods(source), ['A', 'B']);
});

test('the count is the EF Core suite\'s methods, those compiled on the engine and those it runs', () => {
  const files = [`${SUITE_DIR}/Endpoints/GridSearchTests.cs`, `${SUITE_DIR}/Endpoints/CommentEndpointTests.cs`];
  const suite = sqlSuite();
  const readAll = (path) => (path === SQL ? suite.text : read(path));
  assert.deepEqual(suiteCount(SQL, files, readAll), { all: 2, files: allowedOn('Sql').length, filtered: 0, runs: 1 });
});
