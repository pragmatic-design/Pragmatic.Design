using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Turns the accessibility a model carries as text back into the modifier a template emits.
/// </summary>
/// <remarks>
///     ⚠️ Ten-odd templates carry a private <c>ParseAccessibility</c> of their own, each the same four
///     lines. This is that function with one home; it is used by what is written from here on rather
///     than by rewriting those, which is a change none of them asked for. A partial declaration whose
///     accessibility disagrees with the author's does not compile, so the answer has to be the same
///     everywhere — which is the argument for it having one place.
/// </remarks>
internal static class AccessibilityHelper
{
    /// <summary>The modifier for an accessibility spelled as Roslyn spells it, lower-cased.</summary>
    public static AccessModifier Parse(string accessibility)
        => accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
}
