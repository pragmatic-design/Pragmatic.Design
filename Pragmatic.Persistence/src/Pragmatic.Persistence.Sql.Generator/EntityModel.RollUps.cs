using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     <c>[RollUp&lt;TChild&gt;(childProperty, aggregation)]</c> (read by name, the repository's or the library's): a parent column the child's row writes
///     keep, through the foreign key <c>{Parent}Id</c> on the child. The column is filled by the database as far as the parent's writes go.
/// </summary>
internal sealed partial class EntityModel
{
    /// <summary>The parents whose [RollUp] aggregates this entity's rows feed: a set-based command on it would leave them stale.</summary>
    public List<string> RollUpParents { get; private set; } = [];

    /// <summary>The roll-up a property declares: the child type, the child property summed (null for a count).</summary>
    public static (INamedTypeSymbol Child, string? Property)? RollUpOf(IPropertySymbol property)
    {
        var mark = property.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "RollUpAttribute", TypeArguments.Length: 1 });
        if (mark?.AttributeClass!.TypeArguments[0] is not INamedTypeSymbol child)
            return null;
        var count = mark.ConstructorArguments.Length > 1 && mark.ConstructorArguments[1].Value is 1;
        return (child, count ? null : mark.ConstructorArguments.Length > 0 ? mark.ConstructorArguments[0].Value as string : null);
    }

    /// <summary>The problem with a roll-up on <paramref name="parent" />, or null: the child's foreign key and summed property must be there, the parent's key one column.</summary>
    public static string? RollUpProblem(INamedTypeSymbol parent, IPropertySymbol property, INamedTypeSymbol child, string? summed)
    {
        if (!IsEntity(child))
            return $"[RollUp<{child.Name}>] on {parent.Name}.{property.Name}: {child.Name} is not an [Entity]";
        if (!child.GetMembers(parent.Name + "Id").OfType<IPropertySymbol>().Any())
            return $"[RollUp<{child.Name}>] on {parent.Name}.{property.Name}: {child.Name} has no foreign key {parent.Name}Id";
        if (summed is not null && !child.GetMembers(summed).OfType<IPropertySymbol>().Any())
            return $"[RollUp<{child.Name}>] on {parent.Name}.{property.Name}: {child.Name} has no property '{summed}' to sum (a count takes RollUpAggregation.Count)";
        if (parent.GetMembers().OfType<IPropertySymbol>().Count(p => p.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute")) > 1)
            return $"[RollUp<{child.Name}>] on {parent.Name}.{property.Name}: the parent's aggregate is found by a single-column key, {parent.Name} has a composite one";
        if (HierarchyRootOf(parent) is not null || HierarchyRootOf(child) is not null)
            return $"[RollUp<{child.Name}>] on {parent.Name}.{property.Name}: a roll-up joins two plain tables, not a hierarchy's types";
        return null;
    }

    /// <summary>The parents among the compilation's entities and the child's own assembly's (a child from a referenced assembly has its parents there, or here).</summary>
    private void FindRollUpParents(INamedTypeSymbol type, IReadOnlyList<INamedTypeSymbol>? entities)
    {
        var seen = new HashSet<string>();
        foreach (var parent in (entities ?? []).Concat(EntitiesIn(type.ContainingAssembly.GlobalNamespace)))
        {
            foreach (var property in parent.GetMembers().OfType<IPropertySymbol>())
            {
                if (RollUpOf(property) is { } rollUp && SymbolEqualityComparer.Default.Equals(rollUp.Child, type) && seen.Add(parent.Name + "." + property.Name))
                    RollUpParents.Add(parent.Name + "." + property.Name);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EntitiesIn(INamespaceSymbol space)
    {
        foreach (var type in space.GetTypeMembers())
        {
            if (IsEntity(type))
                yield return type;
        }

        foreach (var inner in space.GetNamespaceMembers())
        {
            foreach (var type in EntitiesIn(inner))
                yield return type;
        }
    }
}
