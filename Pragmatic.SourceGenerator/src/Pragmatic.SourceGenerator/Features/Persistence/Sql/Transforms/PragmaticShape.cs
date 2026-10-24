using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's <c>[Entity]</c> as the Sql engine recognises it. Its shape (the key, the stamps, the owner, the scopes, the
///     navigations, the change tracking, the filter classes) is Persistence's own, emitted for every engine and handed to the
///     translation to bind against (<c>EntityShapeArtifacts</c>).
/// </summary>
internal static class PragmaticShape
{
    internal const string EntityAttribute = "Pragmatic.Persistence.Entity.EntityAttribute";

    /// <summary>True for a Pragmatic <c>[Entity]</c>.</summary>
    public static bool IsPragmaticEntity(ITypeSymbol? type)
        => type is INamedTypeSymbol named && named.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == EntityAttribute);

    internal static bool Has(INamedTypeSymbol type, string attribute) => type.GetAttributes().Any(a => a.AttributeClass?.Name == attribute && a.AttributeClass.ContainingNamespace.ToDisplayString() == "Pragmatic.Persistence.Entity");
}
