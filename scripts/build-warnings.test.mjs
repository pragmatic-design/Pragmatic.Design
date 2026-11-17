/**
 * The trim and reflection ratchets count a warning once, whichever target and copy of the log printed it (#832).
 *
 *   node --test scripts/build-warnings.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { distinctWarningsPerProject } from './lib/build-warnings.mjs';

const TRIM = /warning IL(2026|3050)/;
const at = (file, line, code, project, tfm) =>
  `C:\\repo\\${file}(${line},16): warning ${code}: Using member 'X' which has 'RequiresUnreferencedCodeAttribute'. [C:\\repo\\${project}.csproj${tfm ? `::TargetFramework=${tfm}` : ''}]`;

test('a warning printed at the project and again in the summary counts once', () => {
  const line = at('A.cs', 10, 'IL2026', 'Pragmatic.A');
  assert.deepEqual(distinctWarningsPerProject([line, line].join('\n'), TRIM), new Map([['Pragmatic.A', 1]]));
});

test('the same warning on two target frameworks counts once', () => {
  const output = [
    at('M.cs', 41, 'IL2026', 'Pragmatic.Endpoints.OpenApi', 'net10.0'),
    at('M.cs', 41, 'IL2026', 'Pragmatic.Endpoints.OpenApi', 'net11.0'),
    at('M.cs', 41, 'IL2026', 'Pragmatic.Endpoints.OpenApi', 'net10.0'),
    at('M.cs', 41, 'IL2026', 'Pragmatic.Endpoints.OpenApi', 'net11.0'),
  ].join('\n');

  assert.deepEqual(distinctWarningsPerProject(output, TRIM), new Map([['Pragmatic.Endpoints.OpenApi', 1]]));
});

test('two codes on one line, and two lines, are distinct warnings', () => {
  const output = [
    at('M.cs', 41, 'IL2026', 'Pragmatic.A'),
    at('M.cs', 41, 'IL3050', 'Pragmatic.A'),
    at('M.cs', 59, 'IL2026', 'Pragmatic.A'),
  ].join('\n');

  assert.deepEqual(distinctWarningsPerProject(output, TRIM), new Map([['Pragmatic.A', 3]]));
});

test('a warning only one target has still counts', () => {
  const output = at('M.cs', 77, 'IL2026', 'Pragmatic.A', 'net11.0');
  assert.deepEqual(distinctWarningsPerProject(output, TRIM), new Map([['Pragmatic.A', 1]]));
});

test('codes the pattern does not name, and lines with no project, are not counted', () => {
  const output = [at('M.cs', 1, 'IL2075', 'Pragmatic.A'), 'warning IL2026: no project here'].join('\n');
  assert.deepEqual(distinctWarningsPerProject(output, TRIM), new Map());
});
