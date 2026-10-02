/**
 * The attribute-reader measure, and the three false findings its first run produced.
 *
 * ⚠️ 6 attributes and 14 properties on the first pass, of which three attributes and every property
 * were fictional — in two distinct ways, each of which read as a finished number. That is the mistake
 * the reflection ratchet made at 67 with 24% false sites, and its effect was that people argued with
 * the measure instead of closing the gap.
 *
 *   node --test scripts/attribute-readers.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  declaredAttributeClasses, declaredProperties, isAttributeNameIndex, measureAttributeReaders,
  readAttributeNames, withoutOwnDeclaration, withoutShapeDiagnosticCalls,
} from './lib/attribute-readers.mjs';

/** The measure over a handful of synthetic files. */
const measure = (files) => measureAttributeReaders({
  files: files.map(([file, text]) => ({ file, module: 'Pragmatic.Test', text })),
});

const ATTRIBUTE_NAMES = ['shared/SourceGen/AttributeNames.cs', `
  internal static class AttributeNames
  {
      public const string MessageMiddleware = "Pragmatic.Messaging.Attributes.MessageMiddlewareAttribute";
  }`];

const MIDDLEWARE = ['Pragmatic.Messaging/src/MessageMiddlewareAttribute.cs', `
  public sealed class MessageMiddlewareAttribute : Attribute
  {
      public Type? ForMessageType { get; set; }
  }`];

// ── The shape being counted ────────────────────────────────────────────────────────────────────

/**
 * ⚠️ The motivating case. The attribute's metadata name was in `AttributeNames`, and the only use of
 * that constant was the provider whose output is a diagnostic about the class implementing the
 * interface. It compiled, it passed its own check, and no middleware ever ran.
 */
test('an attribute read only by a shape diagnostic counts as unread', () => {
  const m = measure([ATTRIBUTE_NAMES, MIDDLEWARE, ['Pragmatic.SourceGenerator/src/MessagingFeature.cs', `
    public static void Register(Context context)
    {
        RegisterShapeDiagnostic(context, AttributeNames.MessageMiddleware, Helpers.IsClass,
            MessagingShapeDiagnosticTransform.MiddlewareShape);
    }`]]);

  assert.deepEqual(m.unread.map((r) => r.attribute), ['MessageMiddlewareAttribute']);
});

test('the same attribute, once something registers it, is read', () => {
  const m = measure([ATTRIBUTE_NAMES, MIDDLEWARE, ['Pragmatic.SourceGenerator/src/MessagingFeature.cs', `
    public static void Register(Context context)
    {
        RegisterShapeDiagnostic(context, AttributeNames.MessageMiddleware, Helpers.IsClass,
            MessagingShapeDiagnosticTransform.MiddlewareShape);

        var middlewares = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeNames.MessageMiddleware, Helpers.IsClass, MessageMiddlewareTransform.Transform);
    }`]]);

  assert.deepEqual(m.unread, []);
});

/**
 * ⚠️ The half a type-level count reports clean. `ForMessageType` was declared, documented and read by
 * nobody while `MessageMiddlewareAttribute` itself was read.
 */
test('a declared property nothing names counts, even when its type is read', () => {
  const m = measure([ATTRIBUTE_NAMES, MIDDLEWARE, ['Pragmatic.SourceGenerator/src/MessagingFeature.cs', `
    var middlewares = context.SyntaxProvider.ForAttributeWithMetadataName(
        AttributeNames.MessageMiddleware, Helpers.IsClass, MessageMiddlewareTransform.Transform);`]]);

  assert.deepEqual(m.unread, [], 'the type is read');
  assert.deepEqual(m.unreadProperties, ['MessageMiddlewareAttribute.ForMessageType']);
});

test('a property a named argument is read by is read', () => {
  const m = measure([ATTRIBUTE_NAMES, MIDDLEWARE, ['Pragmatic.SourceGenerator/src/MessagingFeature.cs', `
    var middlewares = context.SyntaxProvider.ForAttributeWithMetadataName(
        AttributeNames.MessageMiddleware, Helpers.IsClass, MessageMiddlewareTransform.Transform);`],
  ['Pragmatic.SourceGenerator/src/MessageMiddlewareTransform.cs', `
    var forMessageType = context.Attributes[0].NamedArguments
        .Where(a => a.Key == "ForMessageType")
        .Select(a => a.Value.Value as INamedTypeSymbol)
        .FirstOrDefault();`]]);

  assert.deepEqual(m.unreadProperties, []);
});

// ── The three false findings of the first run ───────────────────────────────────────────────────

/**
 * ⚠️ False finding 1, and it named three real, enforced rules. A validation attribute carries its own
 * behaviour and is reached through the base class the pipeline reads — nothing needs to spell its
 * name. `[SupportedCurrency]` is enforced in Invoicing, and the first run said nothing read it.
 */
test('a rule that overrides its attribute base is read through the base', () => {
  const m = measure([['Pragmatic.Validation/src/SupportedCurrencyAttribute.cs', `
    public sealed class SupportedCurrencyAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value) => true;
    }`]]);

  assert.deepEqual(m.unread, []);
  assert.deepEqual(m.rows[0].readBy, ['overrides ValidationAttribute']);
});

test('a marker that overrides nothing is not excused by having a base', () => {
  const m = measure([['Pragmatic.Test/src/DecorativeAttribute.cs', `
    public sealed class DecorativeAttribute : ValidationAttribute
    {
        public string Note { get; init; } = "";
    }`]]);

  assert.deepEqual(m.unread.map((r) => r.attribute), ['DecorativeAttribute'],
    'deriving is not being reached: the override is the evidence that behaviour runs');
});

/**
 * ⚠️ False finding 2. A get-only property is assigned by the constructor and read back through the
 * constructor's ARGUMENT POSITION — `attribute.ConstructorArguments[0]` — so its name appears nowhere
 * even when the value drives everything. All fourteen properties of the first run were this shape.
 */
test('a get-only property is not counted: it is read by constructor position', () => {
  const m = measure([['Pragmatic.Test/src/CountAttribute.cs', `
    public sealed class CountAttribute : ValidationAttribute
    {
        public CountAttribute(int minimumCount) { MinimumCount = minimumCount; }
        public int MinimumCount { get; }
        public override bool IsValid(object? value) => true;
    }`]]);

  assert.deepEqual(m.unreadProperties, []);
  assert.deepEqual(declaredProperties('public int MinimumCount { get; }'), []);
});

/**
 * ⚠️ False finding 3, the same fourteen from the other side: excluding the declaring file made a
 * property its own `IsValid` reads two lines below look unread.
 */
test('a settable property the attribute\'s own code reads is read', () => {
  const m = measure([['Pragmatic.Test/src/WindowAttribute.cs', `
    public sealed class WindowAttribute : ValidationAttribute
    {
        public int Maximum { get; set; }
        public override bool IsValid(object? value) => value is int n && n <= Maximum;
    }`]]);

  assert.deepEqual(m.unreadProperties, []);
});

test('the declaration itself does not answer the question for a property', () => {
  const own = `
    public sealed class WindowAttribute : Attribute
    {
        public int Maximum { get; set; }
    }`;

  assert.ok(!/\bMaximum\b/.test(withoutOwnDeclaration(own, 'Maximum')),
    'without stripping it, every property is read by definition');
});

// ── The pieces ─────────────────────────────────────────────────────────────────────────────────

test('a shape-diagnostic call is blanked with its nested arguments', () => {
  const blanked = withoutShapeDiagnosticCalls(
    'RegisterShapeDiagnostic(context, AttributeNames.Saga, IsClass(x), (c, t) => Shape(c, t)); Keep(AttributeNames.Saga);');

  assert.ok(!/AttributeNames\.Saga.*\)\s*;\s*Keep/.test(blanked));
  assert.ok(blanked.includes('Keep(AttributeNames.Saga)'), 'only the call is blanked, not the rest');
});

test('the AttributeNames declarations are not reads, and the rest of the file is', () => {
  const { constants, withoutDeclarations } = readAttributeNames(`
    public const string TemporalAsUtc = "Pragmatic.Temporal.Attributes.AsUtcAttribute";
    public static readonly (string, string)[] TemporalBehaviors = [(TemporalAsUtc, "AsUtc")];`);

  assert.equal(constants.get('TemporalAsUtc'), 'Pragmatic.Temporal.Attributes.AsUtcAttribute');
  assert.ok(!withoutDeclarations.includes('AsUtcAttribute'), 'naming a thing is not reading it');
  assert.ok(withoutDeclarations.includes('TemporalBehaviors'), 'the table that two features read stays');
});

/**
 * ⚠️ False finding 4, and the worst of them, because it made the measure read **low** rather than
 * high. There are two index files, and `.endsWith('AttributeNames.cs')` matched
 * `EndpointAttributeNames.cs` first: the real `shared/SourceGen/AttributeNames.cs` was then treated as
 * ordinary source, its 71 declarations were never stripped, and every attribute listed there read as
 * "named in source". The number was 3 either way, which is what makes this kind of bug survive.
 */
test('every file that indexes metadata names is an index, whatever it is called', () => {
  assert.ok(isAttributeNameIndex('shared\\SourceGen\\AttributeNames.cs'));
  assert.ok(isAttributeNameIndex('Pragmatic.SourceGenerator/src/Features/Endpoints/EndpointAttributeNames.cs'));
  assert.ok(!isAttributeNameIndex('Pragmatic.Endpoints/src/Attributes/ApiSummaryAttribute.cs'));
});

test('an attribute indexed in the second index file, and used from there, is read', () => {
  const m = measure([['Pragmatic.SourceGenerator/src/EndpointAttributeNames.cs', `
    internal static class EndpointAttributeNames
    {
        public const string ApiSummary = "Pragmatic.Endpoints.Attributes.ApiSummaryAttribute";
    }`],
  ['Pragmatic.Endpoints/src/ApiSummaryAttribute.cs', 'public sealed class ApiSummaryAttribute : Attribute { }'],
  ['Pragmatic.SourceGenerator/src/EndpointTransform.cs',
    'var s = Read(EndpointAttributeNames.ApiSummary);']]);

  assert.deepEqual(m.unread, []);
});

/**
 * ⚠️ False finding 5, the other direction: strings are kept because a metadata name is a string, and
 * prose inside them names attributes all the time. `Comment("[MessageMiddleware] — the pipeline
 * resolves …")` writes a comment into generated code, and the PRAG0803 message says "decorated with
 * [MessageMiddleware]". Either one alone made the motivating defect invisible.
 */
test('an attribute only talked about in a string is not read', () => {
  const m = measure([ATTRIBUTE_NAMES, MIDDLEWARE, ['Pragmatic.SourceGenerator/src/Template.cs', `
    Comment("[MessageMiddleware] — the pipeline resolves IEnumerable<IMessageMiddleware> and orders by Order");
    Report("Type '{0}' is decorated with [MessageMiddleware] but does not implement IMessageMiddleware");`]]);

  assert.deepEqual(m.unread.map((r) => r.attribute), ['MessageMiddlewareAttribute'],
    'talking about a declaration is not reading it');
});

test('a generic attribute is matched to its arity-suffixed constant', () => {
  const m = measure([['shared/SourceGen/AttributeNames.cs', `
    public const string Raises = "Pragmatic.Authoring.RaisesAttribute\`1";`],
  ['Pragmatic.Abstractions/src/RaisesAttribute.cs', `
    public sealed class RaisesAttribute<TEvent> : Attribute { }`],
  ['Pragmatic.SourceGenerator/src/Feature.cs', 'var p = Pipeline(context, AttributeNames.Raises);']]);

  assert.deepEqual(m.unread, [], 'the metadata name carries `1 and the class declaration does not');
});

test('a class declared abstract is not counted: nobody can write one', () => {
  assert.deepEqual(declaredAttributeClasses('public abstract class ValidationAttribute : Attribute { }', 'f'), []);
});

// ── The repository itself, pinned ──────────────────────────────────────────────────────────────

test('the measure reports no unread attribute and no unread property', () => {
  const m = measureAttributeReaders({ repoRoot: '.' });

  assert.ok(m.total > 250, `the framework declares more than 250 attributes, saw ${m.total}`);
  assert.deepEqual([...m.unread.map((r) => r.attribute)].sort(), [],
    'the list is empty and the budget with it: every attribute that was on it left by being '
    + 'implemented rather than deleted. ⚠️ A name appearing here is a NEW defect, not a '
    + 'regression of the measure: read it before touching anything in scripts/lib/attribute-readers.mjs');
  assert.deepEqual(m.unreadProperties, [],
    'the property half has never fired on the real tree — its cases above are synthetic, and saying '
    + 'so is the difference between a zero and coverage');
});

/**
 * ⚠️ The measure this one is NOT. `[FromBusinessTimezone]` was read all along — by the model binder
 * provider at run time and by the Temporal feature in the generator — while the declaration never
 * reached the record the endpoint deserializes. Reverting that fix moves nothing here, and
 * a reader who expects it to would trust this number for a question it cannot answer.
 */
test('an attribute that is read but does not reach what runs is invisible here', () => {
  const m = measureAttributeReaders({ repoRoot: '.' });
  const temporal = m.rows.find((r) => r.attribute === 'FromBusinessTimezoneAttribute');

  assert.ok(temporal, 'the attribute is there');
  assert.ok(temporal.readBy.length > 0, 'and it reads as read, which is the point of this case');
});
