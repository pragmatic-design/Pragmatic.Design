using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Glossary;

/// <summary>
///     Resolves the name a property actually SERIALIZES to, so the generated AsyncAPI schema describes
///     the payload that is really on the wire: the <c>[JsonPropertyName]</c> value when present, else the
///     CLR name run through the camelCase policy that <c>PragmaticJsonOptions</c> configures for every
///     framework serialization boundary.
/// </summary>
internal static class JsonSchemaNaming
{
    private const string JsonPropertyNameAttribute = "JsonPropertyNameAttribute";
    private const string JsonSerializationNamespace = "System.Text.Json.Serialization";

    /// <summary>The JSON name of <paramref name="property" />.</summary>
    public static string ToJsonName(IPropertySymbol property)
        => ExplicitName(property) ?? ToCamelCase(property.Name);

    /// <summary>
    ///     The camelCase conversion of <see cref="System.Text.Json.JsonNamingPolicy" />.<c>CamelCase</c>,
    ///     reimplemented because the generator targets netstandard2.0 and cannot reference the runtime
    ///     policy. It lowercases the leading run of upper-case letters, not just the first character:
    ///     <c>IOStream</c> serializes as <c>ioStream</c>, and a schema saying <c>iOStream</c> would be wrong.
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                // A following space still terminates the run, but the current char is lowered first.
                if (chars[i + 1] == ' ')
                    chars[i] = char.ToLowerInvariant(chars[i]);

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static string? ExplicitName(IPropertySymbol property)
        => property.GetAttributes()
            .Where(a => a.AttributeClass is { Name: JsonPropertyNameAttribute } cls
                && cls.ContainingNamespace?.ToDisplayString() == JsonSerializationNamespace)
            .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(n => !string.IsNullOrEmpty(n));
}
