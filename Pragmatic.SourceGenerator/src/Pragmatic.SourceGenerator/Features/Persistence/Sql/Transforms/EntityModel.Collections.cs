using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The foreign keys a one-to-many side's children carry: <c>{Parent}Id</c>, or one per key column of a composite parent.</summary>
internal sealed partial class EntityModel
{
    /// <summary>
    ///     The child's foreign key properties towards <paramref name="parent" />, in the parent's key order; null when the child has none. A single-column
    ///     key: <c>{Parent}Id</c>. A composite key: the <c>{Navigation}{KeyProperty}</c> of the child's navigation back to the parent (the to-one side's
    ///     own convention), or <c>{Parent}{KeyProperty}</c>.
    /// </summary>
    public static string[]? CollectionForeignKeys(INamedTypeSymbol parent, INamedTypeSymbol child)
    {
        var single = parent.Name + "Id";
        if (HasProperty(child, single))
            return [single];
        var keys = parent.GetMembers().OfType<IPropertySymbol>().Where(p => p.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute")).Select(p => p.Name).ToList();
        if (keys.Count < 2)
            return null;
        foreach (var back in child.GetMembers().OfType<IPropertySymbol>().Where(p => SymbolEqualityComparer.Default.Equals(p.Type.WithNullableAnnotation(NullableAnnotation.None), parent)))
        {
            var named = keys.Select(k => back.Name + k).ToArray();
            if (named.All(n => HasProperty(child, n)))
                return named;
        }

        var conventional = keys.Select(k => parent.Name + k).ToArray();
        return conventional.All(n => HasProperty(child, n)) ? conventional : null;
    }

    private static bool HasProperty(INamedTypeSymbol type, string name) => type.GetMembers(name).OfType<IPropertySymbol>().Any();
}
