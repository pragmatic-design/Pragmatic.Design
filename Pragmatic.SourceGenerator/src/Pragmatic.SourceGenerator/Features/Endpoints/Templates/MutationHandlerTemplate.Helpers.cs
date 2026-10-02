using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Templates;

/// <summary>
///     Small format/naming helpers for Mutation endpoint handler generation.
/// </summary>
internal sealed partial class MutationHandlerTemplate
{
    private static string EscapeString(string? value) => StringHelper.CSharpLiteral(value);

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        // Escape C# keywords (e.g. a property 'Event' → parameter '@event') so the generated identifier compiles.
        return IdentifierHelper.EscapeIfKeyword(char.ToLowerInvariant(name[0]) + name.Substring(1));
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
