/**
 * How much of the public attribute surface the example applications actually write.
 *
 * In this framework the attribute IS the surface a consumer touches: `[Cacheable]`, `[Query<,>]`,
 * `[assembly: Permission(...)]`. So "do the examples cover the framework?" is answered by counting
 * attributes, not namespaces, not project references.
 *
 * ⚠️ Four ways of measuring this were wrong before this file existed, and each of them
 * read as a confident number:
 *
 *   1. BY NAMESPACE. `Pragmatic.Caching` scored zero while Showcase writes `[Cacheable]` five times:
 *      the attribute arrives through a global using, so the namespace is never spelled.
 *   2. ONLY `[Name`. Every assembly-level attribute counted as unused — `[assembly: GenerateMock<…>]`,
 *      `[assembly: TranslationKeys(…)]`, `[assembly: Permission(…)]` are all written by examples.
 *   3. DECLARATIONS INSIDE DOC COMMENTS. `public sealed class FiscalCodeAttribute` appears in the
 *      `<example>` block of ValidationAttribute.cs. That type does not exist in this repository, and
 *      it was reported as an uncovered attribute of Pragmatic.Validation.
 *   4. `abstract` ATTRIBUTES. Nobody can write one, so counting it guarantees a permanent false gap.
 *
 * ⚠️ A KNOWN BLIND SPOT, left in deliberately: an attribute with an equivalent MSBuild spelling reads
 * as unwritten when the examples use the property. `[assembly: PragmaticGenerateJsonContext]` is the
 * case — four Showcase modules set `<PragmaticGenerateJsonContext>true</…>` and `aot-smoke` relies on
 * `<PublishAot>`, both compiler-visible to a consumer through the generator package's own
 * `build/*.props`, so the property is equivalent everywhere and the feature IS adopted. Writing the
 * attribute into a module that already sets the property, to move the counter, is the decoration this
 * measure exists to expose. Teaching the scanner one attribute's MSBuild twin would be a
 * bespoke rule for one row; the honest cost is this paragraph.
 *
 * A fifth is reported rather than subtracted: attributes the SOURCE GENERATOR emits. No hand-written
 * example will ever contain `[assembly: PragmaticModuleMetadata(`, which BoundaryModuleMetadataTemplate
 * writes into generated code. That bucket is computed from the generated output under
 * `examples/**\/obj/**\/generated/`, so it is EVIDENCE rather than a list somebody maintains — and it
 * is best-effort, because a clean clone has no `obj/`. It never affects the ratchet: the ratchet holds
 * the count written BY AN EXAMPLE, which no absence of generated output can inflate.
 */
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const SKIP_DIRS = new Set(['obj', 'bin', 'node_modules', '.agentflow', '.git']);

/** Every `.cs` under `dir`, skipping build output. */
export function sourceFiles(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name)) continue;
      sourceFiles(join(dir, entry.name), out);
    } else if (entry.name.endsWith('.cs')) out.push(join(dir, entry.name));
  }
  return out;
}

/**
 * Every `.cs` written by one of OUR generators into an example's build output.
 *
 * ⚠️ `generated/` is not enough, and reading it all is a false positive with a very confident face.
 * Alongside `generated/Pragmatic.SourceGenerator/` sits
 * `generated/Microsoft.AspNetCore.OpenApi.SourceGenerators/OpenApiXmlCommentSupport.generated.cs`,
 * which caches the XML documentation of **every referenced assembly** — including the `<example>`
 * blocks of Pragmatic's own attributes. Reading it put `[Inject]`, `[Rule]`, `[UseCase]` and
 * `[ExplicitPermission]` in the "the generator writes it" bucket, which would have quietly retired
 * twelve genuine gaps of Pragmatic.Abstractions.
 */
export function generatedFiles(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === '.git') continue;
      generatedFiles(full, out);
    } else if (entry.name.endsWith('.cs') && /[\\/]generated[\\/]Pragmatic\./.test(full)) out.push(full);
  }
  return out;
}

/**
 * C# with comment and string CONTENT blanked out, newlines kept.
 *
 * Both are needed. A declaration inside an `<example>` block is not a declaration; a doc line reading
 * `/// Demonstrates: [Query] + [Endpoint]` is not a use — and that exact line sits above
 * `SearchPropertiesQuery`, so a measure that reads comments reports the two attributes it names.
 *
 * ⚠️ It is a scanner and not a pair of regexes, and the first version here WAS the pair of regexes.
 * It reported that no example writes `[Cacheable]` — which is the false negative this whole measure
 * exists to avoid — because `/\/\*[\s\S]*?\*\//` starting inside a `//` comment ran on to the next
 * `*​/` anywhere in the concatenated corpus and blanked everything in between. Comment syntax is not
 * a regular language when strings can contain it.
 */
export function blankCommentsAndStrings(text) {
  return blankOut(text, { strings: true });
}

/**
 * The same scanner, keeping string literals.
 *
 * ⚠️ The string branches still have to **run** — they are what stops a `//` or `/*` inside a literal
 * from swallowing the rest of the file — they just do not blank. Which is why this is an option on one
 * scanner rather than a second scanner: `attribute-readers.mjs` needs comments gone and strings kept,
 * because a generator names an attribute by its metadata name, which is a string.
 */
export function blankComments(text) {
  return blankOut(text, { strings: false });
}

function blankOut(text, { strings }) {
  const kept = [...text].map((c) => (c === '\n' || c === '\r' ? c : ' '));
  const out = kept.slice();
  const keep = (from, to) => { for (let k = from; k < to && k < text.length; k++) out[k] = text[k]; };

  let i = 0;
  let plainFrom = 0;

  const blank = (from, to) => { keep(plainFrom, from); plainFrom = to; };
  const blankString = (from, to) => { if (strings) blank(from, to); };

  while (i < text.length) {
    if (text.startsWith('//', i)) {
      let j = i;
      while (j < text.length && text[j] !== '\n' && text[j] !== '\r') j++;
      blank(i, j);
      i = j;
      continue;
    }

    if (text.startsWith('/*', i)) {
      const end = text.indexOf('*/', i + 2);
      const stop = end === -1 ? text.length : end + 2;
      blank(i, stop);
      i = stop;
      continue;
    }

    // Raw string literal: three or more quotes, closed by the same run length.
    const fence = /^"{3,}/.exec(text.slice(i, i + 32))?.[0];
    if (fence) {
      const end = text.indexOf(fence, i + fence.length);
      const stop = end === -1 ? text.length : end + fence.length;
      blankString(i, stop);
      i = stop;
      continue;
    }

    // Verbatim string @"…", where "" escapes a quote. $ may precede or follow the @.
    const verbatim = /^[$@]{1,3}"/.exec(text.slice(i, i + 4))?.[0];
    if (verbatim?.includes('@')) {
      let j = i + verbatim.length;
      while (j < text.length) {
        if (text[j] !== '"') { j++; continue; }
        if (text[j + 1] === '"') { j += 2; continue; }
        break;
      }
      blankString(i, Math.min(j + 1, text.length));
      i = j + 1;
      continue;
    }

    if (text[i] === '"' || text[i] === '\'') {
      const quote = text[i];
      let j = i + 1;
      while (j < text.length && text[j] !== quote && text[j] !== '\n') j += text[j] === '\\' ? 2 : 1;
      blankString(i, Math.min(j + 1, text.length));
      i = j + 1;
      continue;
    }

    i++;
  }

  keep(plainFrom, text.length);
  return out.join('');
}

/**
 * The public attribute types a module declares, by simple name without the `Attribute` suffix.
 *
 * `abstract` is excluded on purpose: `ValidationAttribute` is a base class users derive from, never
 * something they write.
 */
export function declaredAttributes(text) {
  const names = [];
  for (const m of blankCommentsAndStrings(text).matchAll(/public\s+(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)Attribute\b/g))
    names.push(m[1]);
  return names;
}

/**
 * Does this source write the attribute `name`?
 *
 * Two shapes, and both are required:
 *   [Name] · [Name(…)] · [Name<T>] · [assembly: Name(…)] · [property: Name]
 *   [Other, Name]  — a comma-separated attribute list
 *
 * The trailing character class is what keeps `[ExplicitPermission(` from counting as `Permission`
 * and `[MapFromDerived]` from counting as `MapFrom`.
 *
 * ⚠️ A namespace in front counts too — `[global::Pragmatic.Persistence.Query.Attributes.Foo(…)]` —
 * because that is how a **generator** writes one, and the "written by the generator" bucket is
 * measured on generated code. Without it three attributes our own generator emits were counted as
 * gaps nobody had filled, two of them documented as never written by hand. The leading
 * `[` and the trailing class still anchor it, so a mention like `Foo.Bar` is not a use.
 */
export function writesAttribute(text, name) {
  const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const qualifier = '(?:global::)?(?:[A-Za-z_]\\w*\\s*\\.\\s*)*';
  const use = `${qualifier}${escaped}(?:Attribute)?\\s*[(<\\]\\r\\n]`;
  return new RegExp(`\\[\\s*(?:assembly|module|return|type|method|property|field|param|event)?\\s*:?\\s*${use}`).test(text)
    || new RegExp(`\\[[^\\]\\n]*,\\s*${use}`).test(text);
}

/**
 * The whole measure.
 *
 * Returns the three buckets separately, because "written by nobody" is the only one that is a gap:
 * an attribute the generator writes cannot be moved by writing examples.
 */
export function measureCoverage({ repoRoot = '.', exampleText, generatedText } = {}) {
  const modules = [];

  for (const entry of readdirSync(repoRoot, { withFileTypes: true })) {
    if (!entry.isDirectory() || !entry.name.startsWith('Pragmatic.')) continue;
    const src = join(repoRoot, entry.name, 'src');
    if (!existsSync(src)) continue;

    const attributes = new Set();
    for (const file of sourceFiles(src))
      for (const name of declaredAttributes(readFileSync(file, 'utf-8'))) attributes.add(name);

    if (attributes.size === 0) continue;
    modules.push({ module: entry.name, attributes: [...attributes].sort() });
  }

  const examples = exampleText ?? blankCommentsAndStrings(
    sourceFiles(join(repoRoot, 'examples')).map((p) => readFileSync(p, 'utf-8')).join('\n'));

  // Best-effort: absent on a clean clone, and absence only makes the gap list longer, never the
  // ratchet weaker.
  const generated = generatedText ?? blankCommentsAndStrings(generatedFiles(join(repoRoot, 'examples'))
    .map((p) => readFileSync(p, 'utf-8')).join('\n'));

  const rows = modules.map(({ module, attributes }) => {
    const byExample = attributes.filter((a) => writesAttribute(examples, a));
    const rest = attributes.filter((a) => !byExample.includes(a));
    const byGenerator = rest.filter((a) => writesAttribute(generated, a));
    const unwritten = rest.filter((a) => !byGenerator.includes(a));
    return { module, total: attributes.length, byExample, byGenerator, unwritten };
  });

  return {
    rows,
    total: rows.reduce((n, r) => n + r.total, 0),
    byExample: rows.reduce((n, r) => n + r.byExample.length, 0),
    byGenerator: rows.reduce((n, r) => n + r.byGenerator.length, 0),
    unwritten: rows.reduce((n, r) => n + r.unwritten.length, 0),
    generatedOutputSeen: generated.length > 0,
  };
}
