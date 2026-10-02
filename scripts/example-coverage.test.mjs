/**
 * The example-coverage measure, and the four ways it was wrong before it lived here.
 *
 * ⚠️ Every case below is a correction that was found by checking a gap before filing it, not by
 * re-reading the script. A coverage number with known false readings makes people argue
 * with the measure instead of closing the gap — which is how the reflection ratchet started at 67
 * with 24% of its sites fictional.
 *
 *   node --test scripts/example-coverage.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  declaredAttributes, measureCoverage, sourceFiles, blankCommentsAndStrings, writesAttribute,
} from './lib/example-coverage.mjs';
import {
  COVERAGE_REASONS, PACKAGE_REASONS, reasonsWithMissingProofs,
} from './lib/example-coverage-reasons.mjs';

// ── What counts as writing an attribute ────────────────────────────────────────────────────────

test('an assembly-level attribute counts', () => {
  const source = '[assembly: GenerateMock<IBillingInternalActions>]';

  assert.ok(writesAttribute(source, 'GenerateMock'),
    'matching only `[Name` called GenerateMock, TranslationKeys and Permission unused while the '
    + 'examples write all three at assembly level');
});

test('every other attribute target counts too', () => {
  for (const target of ['module', 'return', 'property', 'field', 'method', 'param', 'event'])
    assert.ok(writesAttribute(`[${target}: NotLogged]`, 'NotLogged'), target);
});

test('a comma-separated list counts both of its attributes', () => {
  const source = '[Cacheable(60), CacheKey("tenant")]';

  assert.ok(writesAttribute(source, 'Cacheable'));
  assert.ok(writesAttribute(source, 'CacheKey'));
});

test('the Attribute suffix is optional at the use site', () => {
  assert.ok(writesAttribute('[CacheableAttribute]', 'Cacheable'));
});

test('a generic attribute counts', () => {
  assert.ok(writesAttribute('[IncludeModule<BookingModule>]', 'IncludeModule'));
});

test('a bare attribute at the end of a line counts', () => {
  assert.ok(writesAttribute('    [ToClientTimezone]\r\n    public DateTime At { get; init; }', 'ToClientTimezone'));
});

/**
 * ⚠️ A generator writes every attribute fully qualified, and that is how the whole
 * "the generator writes it" bucket is counted. Unqualified-only matching read three such attributes
 * as gaps nobody had filled — including two whose own summaries say they are written by the
 * generator and never by hand.
 */
test('a fully qualified attribute counts, which is how a generator writes one', () => {
  assert.ok(writesAttribute(
    '[global::Pragmatic.Persistence.Query.Attributes.ProjectableBody("__pragmatic_source__.X")]',
    'ProjectableBody'));
  assert.ok(writesAttribute('[Pragmatic.Persistence.EFCore.PragmaticDbContext("Billing")]',
    'PragmaticDbContext'));
  assert.ok(writesAttribute('[global::Pragmatic.IncludeModule<BookingModule>]', 'IncludeModule'));
});

/**
 * ⚠️ The control for the whole measure. Without it, "does the text contain the name" would report
 * 100% coverage of everything whose name is a prefix of something else.
 */
test('a longer attribute does not count as the shorter one it starts with', () => {
  assert.ok(!writesAttribute('[ExplicitPermission("leave.read")]', 'Permission'),
    'ExplicitPermission is a different attribute of a different feature');
  assert.ok(!writesAttribute('[MapFromDerived]', 'MapFrom'));
  assert.ok(writesAttribute('[ExplicitPermission("leave.read")]', 'ExplicitPermission'),
    'and the longer one still counts as itself');
});

test('a commented-out attribute is not a use', () => {
  const text = blankCommentsAndStrings('// [Sse]\n/* [ResponseCache(30)] */\npublic sealed class Foo;');

  assert.ok(!writesAttribute(text, 'Sse'));
  assert.ok(!writesAttribute(text, 'ResponseCache'));
});

// ── What counts as a declared attribute ────────────────────────────────────────────────────────

test('a declaration inside a doc comment is not a declaration', () => {
  const source = [
    '/// <example>',
    '/// public sealed class FiscalCodeAttribute : ValidationAttribute',
    '/// </example>',
    'public abstract class ValidationAttribute : Attribute;',
  ].join('\n');

  assert.deepEqual(declaredAttributes(source), [],
    'FiscalCodeAttribute does not exist in this repository and was reported as an uncovered '
    + 'attribute of Pragmatic.Validation');
});

test('an abstract attribute is not counted at all', () => {
  assert.deepEqual(declaredAttributes('public abstract class ValidationAttribute : Attribute;'), [],
    'nobody can write an abstract attribute, so counting it is a permanent false gap');
});

test('sealed, plain and partial public attributes are all counted', () => {
  const source = [
    'public sealed class CacheableAttribute : Attribute;',
    'public class RuleAttribute : Attribute;',
    'public partial class SseAttribute : Attribute;',
    'internal sealed class HiddenAttribute : Attribute;',
  ].join('\n');

  assert.deepEqual(declaredAttributes(source).sort(), ['Cacheable', 'Rule', 'Sse'],
    'internal is not consumer surface');
});

// ── What the file walk sees ────────────────────────────────────────────────────────────────────

test('build output is never read as example source', () => {
  const files = sourceFiles('examples');

  assert.ok(files.length > 0, 'the examples are there');
  assert.ok(!files.some((f) => /[\\/](obj|bin)[\\/]/.test(f)),
    'the XML doc cache under obj/ quotes every attribute of the framework in its own documentation, '
    + 'so reading it would report near-total coverage');
});

// ── The repository itself, pinned ──────────────────────────────────────────────────────────────

/**
 * ⚠️ The case that moved the measure off namespaces. Showcase writes `[Cacheable]` five times and
 * names `Pragmatic.Caching` nowhere, because the attribute arrives through a global using.
 */
test('Showcase writing [Cacheable] counts as covering Pragmatic.Caching', () => {
  const examples = blankCommentsAndStrings(
    sourceFiles('examples').map((p) => readFileSync(p, 'utf-8')).join('\n'));

  assert.ok(writesAttribute(examples, 'Cacheable'),
    'if this ever goes false, check whether the attribute moved before touching the measure');
});

test('the measure reports three buckets and they add up', () => {
  const m = measureCoverage({ repoRoot: '.' });

  assert.equal(m.byExample + m.byGenerator + m.unwritten, m.total);
  assert.ok(m.total > 200, `the framework declares more than 200 attributes, saw ${m.total}`);
  assert.ok(m.rows.every((r) => r.byExample.length + r.byGenerator.length + r.unwritten.length === r.total));
});

// ── The register of reasons, and the half that keeps it honest ────────────────────────────────

/**
 * ⚠️ A register of reasons is the perfect hiding place: a shortfall with a sentence beside it reads
 * as answered, and nobody re-reads a sentence. These three cases are what stops one settling.
 */
test('a reason naming a proof that does not exist is reported', () => {
  const missing = reasonsWithMissingProofs((name) => name !== 'NoSuchProofTests');

  assert.equal(missing.length, 0, 'every real proof is found when the lookup says so');

  const withAFake = reasonsWithMissingProofs(() => false);
  assert.ok(withAFake.length > 0,
    'with nothing found, every recorded proof is reported — the check is not vacuous');
  assert.ok(withAFake.every((m) => m.attribute && m.proof),
    'each report names the declaration and the proof, so it can be acted on without a search');
});

test('every recorded reason names at least one proof', () => {
  for (const [key, entry] of [...Object.entries(COVERAGE_REASONS), ...Object.entries(PACKAGE_REASONS)]) {
    assert.ok(entry.provedBy.length > 0,
      `${key} records a reason and no proof, which is an excuse rather than a decision`);
    assert.ok(entry.why.length > 40,
      `${key}'s reason is too short to say what scenario is missing`);
  }
});

/**
 * The register may only speak about declarations the measure actually reports as unwritten.
 */
test('every reason is about an attribute no example writes', () => {
  const unwritten = new Set(measureCoverage({ repoRoot: '.' }).rows.flatMap((r) => r.unwritten));

  for (const key of Object.keys(COVERAGE_REASONS)) {
    assert.ok(unwritten.has(key),
      `${key} carries a reason for not being written, and an example writes it — take the reason out`);
  }
});

test('Pragmatic.Validation does not declare a FiscalCode attribute', () => {
  const validation = measureCoverage({ repoRoot: '.' }).rows.find((r) => r.module === 'Pragmatic.Validation');

  assert.ok(validation, 'the module is there');
  assert.ok(!validation.unwritten.includes('FiscalCode'),
    'it only ever existed inside an <example> block; this pins the doc-comment correction against '
    + 'the real tree, where the synthetic case above cannot');
});
