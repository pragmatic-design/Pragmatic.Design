/**
 * The rule that decides the silent-drop number, exercised on a few lines instead of on the tree.
 *
 * ⚠️ The counter counted the one file whose whole job is to report. `MessagingShapeDiagnosticTransform`
 * answers with a value-equatable carrier and never constructs a `Diagnostic` — it cannot, because a
 * `Diagnostic` in an incremental pipeline breaks caching — so the pattern that exists **because**
 * diagnostics must cache correctly read as silence, and its five sites took the budget to exactly full.
 * The same shape as the reflection ratchet's first version, which counted comments saying
 * an API was not used: a counter with false positives makes people fight the measure.
 *
 * So the two halves are held here: a carrier is a channel, and nothing else quietly became one.
 *
 *   node --test scripts/silent-drops.test.mjs
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classify } from './silent-drops.mjs';

const transform = (body) => `
using Microsoft.CodeAnalysis;
internal static class SomeTransform
{
${body}
}
`;

/**
 * Every reported line is a line that really holds a drop.
 *
 * ⚠️ Asserted on what the line **contains**, not on its number: pinning arithmetic over a template
 * tests how this file is indented, and the claim is that a `file:line` sent to an editor lands on the
 * `return null;` it names.
 */
const holdsTheDrops = (text, lines) => {
  const all = text.split('\n');
  assert.ok(lines.length > 0, 'at least one drop was reported');
  for (const line of lines)
    assert.match(all[line - 1] ?? '', /return null;/, `line ${line} holds the drop it reports`);
  return lines;
};

test('a transform that answers null and says nothing is counted', () => {
  const text = transform(`
    public static SomeModel? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        return new SomeModel(symbol.Name);
    }`);
  const { isTransform, hasChannel, lines } = classify(text);

  assert.equal(isTransform, true);
  assert.equal(hasChannel, false);
  assert.equal(holdsTheDrops(text, lines).length, 1, 'one drop, and it is the guard');
});

test('a transform that constructs a Diagnostic has a channel', () => {
  const { hasChannel } = classify(transform(`
    public static SomeModel? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        context.ReportDiagnostic(Diagnostic.Create(Descriptors.Bad, null));
        return null;
    }`));

  assert.equal(hasChannel, true);
});

test('a transform that answers with a diagnostic carrier has a channel too', () => {
  const { isTransform, hasChannel } = classify(transform(`
    public static MessagingDiagnosticInfo? HandlerShape(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        return new MessagingDiagnosticInfo(MessagingDiagnosticKind.HandlerMustImplementInterface, null, []);
    }`));

  assert.equal(isTransform, true);
  assert.equal(hasChannel, true, 'the carrier is the pattern an incremental pipeline forces');
});

test('a file that only mentions a carrier does not get a channel from mentioning it', () => {
  const { hasChannel } = classify(transform(`
    private static readonly string Note = "see MessagingDiagnosticInfo for the carrier pattern";

    public static SomeModel? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        return null;
    }`));

  assert.equal(hasChannel, false, 'the rule is the transform’s return type, not the word appearing');
});

test('a comment that names Diagnostic to say it is not reported is not a channel', () => {
  const text = transform(`
    // No Diagnostic here: the caller reports.
    public static SomeModel? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        return null;
    }`);
  const { hasChannel, lines } = classify(text);

  assert.equal(hasChannel, false, 'naming it in a remark is not having one');
  assert.equal(holdsTheDrops(text, lines).length, 1);
});

test('a `return null;` inside a comment is not counted, and the line reported is the code one', () => {
  const text = transform(`
    /// <summary>Answers <c>return null;</c> when there is nothing to say.</summary>
    public static SomeModel? Extract(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        return null;
    }`);
  const { lines } = classify(text);

  assert.equal(lines.length, 1, 'the one in the summary is not a drop');
  const reported = text.split('\n')[lines[0] - 1];
  assert.match(reported, /return null;/);
  assert.doesNotMatch(reported, /summary/, 'and it is the statement, not the remark that quotes it');
});

test('a file that is not an attribute-triggered transform is not counted at all', () => {
  const { isTransform } = classify(`
internal static class Helpers
{
    public static string? Name(ISymbol symbol) => null;
}
`);

  assert.equal(isTransform, false);
});
