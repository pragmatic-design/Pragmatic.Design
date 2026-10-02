using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The name a property carries on the wire, when it is not the property's own.
/// </summary>
/// <remarks>
///     <para>
///         One reader, because the answer has to be the same everywhere it is asked. It is asked in
///         two places that describe the same declaration to two audiences — the request body the
///         endpoint deserializes, and the schema the document publishes — and when only one of them
///         asked, the endpoint accepted a name the contract never mentioned.
///     </para>
///     <para>
///         The attribute is matched by name <em>and</em> namespace rather than by
///         <c>ToDisplayString()</c>, which is the convention the rest of this generator follows.
///     </para>
///     <para>
///         ⚠️ It resolves only when <c>System.Text.Json</c> is among the compilation's references.
///         In production it always is; in a generator test harness it is whatever that test passed,
///         and an unresolved attribute yields no <c>AttributeClass</c> — so the wire name comes back
///         null and the test reads as "the feature does not work". Three attempts at a unit test for
///         this failed that way before the reference was the answer.
///     </para>
/// </remarks>
internal static class WireNameReader
{
    private const string AttributeName = "JsonPropertyNameAttribute";
    private const string AttributeNamespace = "System.Text.Json.Serialization";

    /// <summary>The declared wire name, or null when the property is not renamed.</summary>
    public static string? Read(ISymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: AttributeName } declaration) continue;
            if (declaration.ContainingNamespace?.ToDisplayString() != AttributeNamespace) continue;

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string name
                && !string.IsNullOrWhiteSpace(name))
                return name;
        }

        return null;
    }
}
