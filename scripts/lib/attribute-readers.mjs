/**
 * Which of the attributes this framework declares are actually **read** by something.
 *
 * The shape this counts: an attribute the framework declares, that nothing reads except a
 * check on how it is written. It compiles, it passes its own diagnostic, it appears in IntelliSense
 * and in the published documentation, and it does nothing — and there is no failure to observe from
 * outside, which is why `[MessageMiddleware]` survived: the messages were still handled, just not
 * wrapped. `[Cacheable]` on a method nobody registers would be the same class of defect.
 *
 * ⚠️ **This does not catch a declaration that is read but never reaches what runs.** `[FromBusinessTimezone]` was read
 * the whole time — by `TemporalConversionModelBinderProvider` at run time and by `TemporalFeature` in
 * the generator — and the defect was that the declaration never reached the type the endpoint
 * deserializes. "Is it read" and "does it reach the thing that runs" are two questions. Only the
 * first is countable here; the second is what an example plus a test answers, which is the example
 * coverage measure.
 *
 * **What counts as read**, and all of it is evidence in the source rather than a list somebody keeps:
 *
 *   1. the token `FooAttribute` appears anywhere in `Pragmatic.*​/src/**` or `shared/SourceGen/**`
 *      outside the file that declares it — a `typeof`, a `nameof`, a pattern match, a metadata-name
 *      string, an analyzer's constant. Strings are kept for exactly this reason: a generator names an
 *      attribute by its metadata name, which is a string;
 *   2. or the `AttributeNames` constant whose value names it is used somewhere.
 *
 * And what does **not** count:
 *
 *   - the declaration itself, including the `public const string Foo = "…FooAttribute";` line in
 *     `AttributeNames.cs`. Naming a thing is not reading it — that line is the point of the file;
 *   - anything inside a `RegisterShapeDiagnostic(…)` call. That helper registers a provider whose only
 *     output is a diagnostic about how the attribute is written, so an attribute whose only appearance
 *     is there is precisely the defect being counted.
 *
 * ⚠️ **Deliberately undercounting.** A token match says "something names this", not "something acts on
 * it", so the number is a floor: every attribute reported unread is unread, and some that are read
 * only in a dead branch are not reported. That is the right direction for a first number — the
 * reflection ratchet opened at 67 with 24% of its sites fictional, and the effect was that people
 * argued with the measure instead of closing the gap.
 *
 * ⚠️ **A property named like a common word reads as read.** `Order`, `Name`, `Value` appear everywhere,
 * so an unread property with such a name is invisible here. The property half of the measure exists
 * because `MessageMiddlewareAttribute.ForMessageType` was declared, documented and read by nobody
 * while the type around it *was* read — a type-level count reported it clean. The blind spot is the
 * honest cost of not keeping a list.
 */
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { sourceFiles, blankComments } from './example-coverage.mjs';

/** The helper whose whole output is a diagnostic about the attribute's shape. */
const SHAPE_DIAGNOSTIC_CALL = 'RegisterShapeDiagnostic';

/**
 * Declarations of public, non-abstract attribute classes, with the file each is in.
 *
 * Returns `{ name, file }` where `name` carries the `Attribute` suffix, because that is the token
 * everything that reads an attribute spells: `typeof(CacheableAttribute)`, `.Name == "CacheableAttribute"`,
 * and the tail of every metadata name.
 */
export function declaredAttributeClasses(text, file) {
  const found = [];
  const source = blankComments(text);

  for (const m of source
    .matchAll(/public\s+(?:sealed\s+)?(?:partial\s+)?class\s+(\w+Attribute)\b\s*(?:\([^)]*\))?\s*(?::\s*(\w+))?/g))
    found.push({ name: m[1], file, base: m[2] ?? null, overrides: /\b(?:public|protected)\s+override\b/.test(source) });

  return found;
}

/**
 * The **settable** public properties an attribute class declares.
 *
 * ⚠️ Settable is the whole point, and the first version counted every property. A get-only property is
 * assigned by the constructor and read back through the constructor's **argument position** — a
 * generator says `attribute.ConstructorArguments[0]`, and the property's name appears nowhere in the
 * repository even when the value drives everything. Counting those put `CountAttribute.MinimumCount`
 * and `LengthAttribute.MaximumLength` on the list, which `IsValid` reads two lines below their own
 * declaration. A property with a setter can only arrive as a **named** argument, so it can only be
 * read by name — which is why it is the half that can be measured, and `ForMessageType`, the member
 * this measure exists for, is one.
 *
 * Read off the whole file rather than the class body: an attribute file holds one type (one type per file), and a
 * brace matcher here would be a parser for no gain.
 */
export function declaredProperties(text) {
  const names = [];
  for (const m of blankComments(text)
    .matchAll(/public\s+(?!const\b|static\b)[\w?<>,.\[\]\s]+?\s(\w+)\s*\{([^}]*)\}/g))
    if (/\b(?:set|init)\b/.test(m[2])) names.push(m[1]);
  return [...new Set(names)];
}

/**
 * The attribute's own file with this property's declaration and constructor assignment removed.
 *
 * ⚠️ Its own file has to be searched — a property the attribute's own `IsValid` reads is read — and
 * without this the declaration itself would answer the question for every property.
 */
export function withoutOwnDeclaration(text, property) {
  return text
    .replace(new RegExp(`public[^;{}]*\\b${property}\\s*\\{[^}]*\\}(\\s*=[^;]*;)?`, 'g'), ' ')
    .replace(new RegExp(`^\\s*${property}\\s*=[^;]*;`, 'gm'), ' ');
}

/**
 * Is this file an index of metadata names rather than a reader of them?
 *
 * ⚠️ There are **two**, and matching only the path's tail found the wrong one: `.endsWith` matched
 * `Features/Endpoints/EndpointAttributeNames.cs` first, so `shared/SourceGen/AttributeNames.cs` was
 * treated as ordinary source, its declarations were never stripped, and every one of the 71
 * attributes indexed there read as "named in source". The measure was almost inert and said 3, which
 * looked like a good number. A file whose job is to hold the names is an index whatever it is called.
 */
export function isAttributeNameIndex(file) {
  return /AttributeNames\.cs$/.test(file);
}

/**
 * An index file split into its constants and everything else.
 *
 * The constant declarations are removed from the text: `public const string MessageMiddleware =
 * "….MessageMiddlewareAttribute";` names the attribute without reading it, and counting it would make
 * every attribute the generator knows about read by definition. What is left — the `TemporalBehaviors`
 * table, for one — is a genuine consumer, so it stays in the corpus.
 */
export function readAttributeNames(text) {
  const constants = new Map();
  const withoutDeclarations = blankComments(text).replace(
    /public\s+const\s+string\s+(\w+)\s*=\s*"([^"]*)"\s*;/g,
    (_, name, value) => {
      constants.set(name, value);
      return ' '.repeat(name.length + value.length);
    });

  return { constants, withoutDeclarations };
}

/**
 * The text with every `RegisterShapeDiagnostic(…)` call blanked, parentheses balanced.
 *
 * A regex cannot do this: the argument lists hold nested calls and lambdas.
 */
export function withoutShapeDiagnosticCalls(text) {
  let out = text;
  for (;;) {
    const start = out.indexOf(`${SHAPE_DIAGNOSTIC_CALL}(`);
    if (start === -1) return out;

    let depth = 0;
    let i = start + SHAPE_DIAGNOSTIC_CALL.length;
    for (; i < out.length; i++) {
      if (out[i] === '(') depth++;
      else if (out[i] === ')' && --depth === 0) { i++; break; }
    }

    out = out.slice(0, start) + ' '.repeat(i - start) + out.slice(i);
  }
}

/** Every `.cs` of the framework's own source, and of the code linked into its generators. */
export function frameworkSources(repoRoot) {
  const files = [];
  for (const entry of readdirSync(repoRoot, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('Pragmatic.')) continue;
    const src = join(repoRoot, entry.name, 'src');
    if (existsSync(src)) files.push(...sourceFiles(src).map((f) => ({ file: f, module: entry.name })));
  }

  const shared = join(repoRoot, 'shared');
  if (existsSync(shared)) files.push(...sourceFiles(shared).map((f) => ({ file: f, module: 'shared' })));

  return files;
}

/** Is `token` in any of these texts as a whole word, skipping the file that declares it? */
function namedIn(corpus, token, exceptFile) {
  const pattern = new RegExp(`\\b${token}\\b`);
  for (const entry of corpus)
    if (entry.file !== exceptFile && pattern.test(entry.text)) return true;
  return false;
}

/**
 * Is the index constant `constant` **used** anywhere?
 *
 * ⚠️ As a member access outside an index file — `AttributeNames.MessageMiddleware` — and not as a bare
 * word, because strings are kept and prose inside them names attributes constantly. The two that
 * defeated the first version: `Comment("[MessageMiddleware] — the pipeline resolves …")`, which writes
 * a comment into generated code, and the PRAG0803 message `"Type '{0}' is decorated with
 * [MessageMiddleware] but does not implement IMessageMiddleware"`. Both are the attribute being talked
 * about, not read, and either one alone made the motivating defect invisible.
 *
 * Inside an index file the reference is bare, because it is a sibling: `(TemporalAsUtc, "AsUtc")` in
 * the `TemporalBehaviors` table is the Endpoints feature's only way to that attribute.
 */
function constantUsed(corpus, constant, exceptFile) {
  const asMember = new RegExp(`\\.\\s*${constant}\\b`);
  const asSibling = new RegExp(`\\b${constant}\\b`);

  for (const entry of corpus) {
    if (entry.file === exceptFile) continue;
    if (entry.index ? asSibling.test(entry.text) : asMember.test(entry.text)) return true;
  }
  return false;
}

/** The constant whose metadata name is this attribute, arity and namespace aside. */
function constantsNaming(constants, attribute) {
  const bare = (value) => value.replace(/`\d+$/, '');
  return [...constants]
    .filter(([, value]) => bare(value) === attribute || bare(value).endsWith(`.${attribute}`))
    .map(([constant]) => constant);
}

/**
 * The whole measure.
 *
 * Every attribute is read or unread, and every read one carries the properties nothing names.
 *
 * ⚠️ The corpus is blanked **once**. Building it per attribute is the same answer and a hundredfold
 * the work, and the first version here did exactly that.
 */
export function measureAttributeReaders({ repoRoot = '.', files } = {}) {
  const sources = files ?? frameworkSources(repoRoot).map(({ file, module }) => ({
    file, module, text: readFileSync(file, 'utf-8'),
  }));

  const attributes = [];
  const declarationText = new Map();

  for (const { file, module, text } of sources) {
    for (const declared of declaredAttributeClasses(text, file)) {
      attributes.push({ ...declared, module });
      declarationText.set(file, blankComments(text));
    }
  }

  // An index of metadata names is not a reader of them: its declarations come out, and whatever else
  // it holds — the `TemporalBehaviors` table — stays, because two features read that.
  const constants = new Map();
  const corpus = sources.map((s) => {
    if (!isAttributeNameIndex(s.file))
      return { file: s.file, index: false, text: withoutShapeDiagnosticCalls(blankComments(s.text)) };

    const index = readAttributeNames(s.text);
    for (const [name, value] of index.constants) constants.set(name, value);
    return { file: s.file, index: true, text: withoutShapeDiagnosticCalls(index.withoutDeclarations) };
  });

  const rows = attributes.map(({ name, file, module, base, overrides }) => {
    const own = declarationText.get(file) ?? '';
    const readBy = [];

    if (namedIn(corpus, name, file)) readBy.push('named in source');
    for (const constant of constantsNaming(constants, name))
      if (constantUsed(corpus, constant, file)) readBy.push(`AttributeNames.${constant}`);

    // ⚠️ A rule that derives from one of our attribute base classes and overrides a member of it is
    // reached through whoever reads the base — nothing needs to name it. Counting those put the three
    // money validators on the list, each of which overrides `IsValid` and works: `[SupportedCurrency]`
    // is enforced by the validation pipeline calling the base, and Invoicing depends on it.
    if (overrides && base && base !== 'Attribute' && base.endsWith('Attribute'))
      readBy.push(`overrides ${base}`);

    const unreadProperties = readBy.length === 0
      ? []
      : declaredProperties(own).filter((p) =>
        !namedIn(corpus, p, file) && !namedIn([{ file: null, text: withoutOwnDeclaration(own, p) }], p, file));

    return { attribute: name, module, file, readBy, unreadProperties };
  });

  return {
    rows,
    total: rows.length,
    unread: rows.filter((r) => r.readBy.length === 0),
    unreadProperties: rows.flatMap((r) => r.unreadProperties.map((p) => `${r.attribute}.${p}`)),
  };
}
