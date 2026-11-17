/**
 * The forms C# 15 reads differently (#820), as the generated-code check finds them.
 *
 *   node --test scripts/csharp15-forms.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { csharp15Forms, csharp15FormsInGenerated } from './csharp15-forms.mjs';

const found = (source) => csharp15Forms(source).map((f) => f.form);

test('a with(...) element of a collection expression is found, first or later', () => {
  // The breaking change page's own example.
  assert.equal(found('items = [with(x, y), z];').length, 1);
  assert.equal(found('items = [z, with(x, y)];').length, 1);
});

test('the escaped @with call and a record with-expression are not', () => {
  assert.deepEqual(found('items = [@with(x, y), z];'), []);
  assert.deepEqual(found('var copy = original with { Name = "x" };'), []);
});

test('a type or alias named union or closed is found', () => {
  for (const source of ['public class union { }', 'internal sealed record closed;', 'using closed = Some.Type;'])
    assert.equal(found(source).length, 1, source);
});

test('a field or property of type union or closed is found', () => {
  // The page's examples: `union OldField;` and `closed oldField;`.
  for (const source of ['    union OldField;', '    closed oldField;', '    public closed State { get; }', '    private readonly union Value = default;'])
    assert.equal(found(source).length, 1, source);
});

test('the escaped spellings, and union or closed used as plain identifiers, are not', () => {
  for (const source of [
    'class @closed { }',
    '    @union CurrentField;',
    '    @closed currentField;',
    'var closed = true;',
    'if (closed) return;',
    'closedCount = 1;',
    'public bool Closed { get; }',
  ])
    assert.deepEqual(found(source), [], source);
});

test('nameof(this.…) in an attribute is found, nameof outside one is not', () => {
  assert.equal(found('    [System.Obsolete(nameof(this.P))]').length, 1);
  assert.equal(found('    [System.Obsolete(nameof(base.P))]').length, 1);
  assert.deepEqual(found('    [System.Obsolete(nameof(P))]'), []);
  assert.deepEqual(found('    var n = nameof(this.P);'), []);
});

test('a parenthesised name followed by when is found', () => {
  assert.equal(found('        (X.Y) when a > b => 1,').length, 1);
  assert.deepEqual(found('        X.Y when a > b => 1,'), []);
});

test('comments are not read', () => {
  assert.deepEqual(found('// items = [with(x, y), z];'), []);
  assert.deepEqual(found('/// <summary>class union</summary>'), []);
  assert.deepEqual(found(' * closed oldField;'), []);
});

/** An obj folder with one generated file per generator: { 'Pragmatic.X': source, 'Microsoft.Y': source }. */
function objWith(generated) {
  const root = mkdtempSync(join(tmpdir(), 'csharp15-forms-'));
  for (const [generator, source] of Object.entries(generated)) {
    const dir = join(root, 'App', 'debug', 'generated', generator, `${generator}.Generator`);
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'Output.g.cs'), source);
  }
  return root;
}

test('the build output of a Pragmatic generator is read, and a form in it found', () => {
  const root = objWith({ 'Pragmatic.SourceGenerator': 'class C\n{\n    closed oldField;\n}\n' });
  try {
    const { files, offenders } = csharp15FormsInGenerated(root);
    assert.equal(files, 1);
    assert.equal(offenders.length, 1);
    assert.match(offenders[0].site, /Output\.g\.cs:3$/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('another generator\'s output is not read', () => {
  const root = objWith({ 'Microsoft.AspNetCore.OpenApi.SourceGenerators': 'items = [with(x, y), z];' });
  try {
    assert.deepEqual(csharp15FormsInGenerated(root), { files: 0, offenders: [] });
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('a folder with no generated output reads zero files, so the gate can refuse it', () => {
  const root = mkdtempSync(join(tmpdir(), 'csharp15-forms-'));
  try {
    assert.equal(csharp15FormsInGenerated(root).files, 0);
    assert.equal(csharp15FormsInGenerated(join(root, 'missing')).files, 0);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('each finding names its line and the fix', () => {
  const [finding] = csharp15Forms('namespace N;\nclass C\n{\n    closed oldField;\n}\n');
  assert.equal(finding.line, 4);
  assert.match(finding.why, /@closed/);
});
