using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     What the shape of Pragmatic's entities needs from its generator: every type of a namespace, and Pragmatic's plural. Kept apart so the
///     shape's files (<c>PragmaticShape</c>, <c>PragmaticRelations</c>, <c>PragmaticFilters</c>, <c>PragmaticPermissions</c>) compile in the
///     documents generator too, which links them: one shape for both engines.
/// </summary>
internal static class PragmaticNames
{
    public static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;
        foreach (var nested in ns.GetNamespaceMembers())
        {
            foreach (var type in AllTypes(nested))
                yield return type;
        }
    }

    /// <summary>A C# string literal of <paramref name="text" />, as <c>SqlInterceptorGenerator.Literal</c> writes it.</summary>
    public static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Pragmatic's <c>StringHelper.Pluralize</c>: the table of an entity, the name of a collection.</summary>
    public static string Pluralize(string name)
    {
        if (name.EndsWith("y", System.StringComparison.Ordinal) && name.Length > 1 && !"aeiou".Contains(name[name.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";
        if (name.EndsWith("s", System.StringComparison.Ordinal) || name.EndsWith("x", System.StringComparison.Ordinal) || name.EndsWith("z", System.StringComparison.Ordinal) || name.EndsWith("ch", System.StringComparison.Ordinal) || name.EndsWith("sh", System.StringComparison.Ordinal))
            return name + "es";
        return name + "s";
    }
}
