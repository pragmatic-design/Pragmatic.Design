using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
///     Which group each entity's hand-written operations are in, read once for the compilation.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A group is inferred from the <b>operation's</b> namespace, and an entity's namespace is
///         flat whatever folder the file sits in — <c>{Module}.Entities</c> by rule. So a scaffolded
///         operation, which takes the entity's namespace, has no segment to infer from and lands on the
///         boundary root: <c>catalog.Amenities.CreateAmenity(…)</c> beside
///         <c>catalog.ResourceListAmenity(…)</c>, one grouped and one not, only because one was
///         generated.
///     </para>
///     <para>
///         The answer already exists in the module: the operations the author wrote for that entity say
///         which group it belongs to. This reads that, and the scaffolding <b>carries</b> it through
///         <c>SubBoundaryName</c> — the field the boundary already prefers over the namespace — rather
///         than inferring a second time from something it does not have.
///     </para>
///     <para>
///         An entity nobody wrote an operation for has no group, and its scaffolding stays on the root.
///         Inventing one would name a group after a folder that does not exist.
///     </para>
/// </remarks>
internal static class EntityGroupReader
{
    /// <summary>Entity full type name (no <c>global::</c>) → the namespace of an operation on it.</summary>
    public static Dictionary<string, string> Read(Compilation compilation, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in EnumerateSourceTypes(compilation.Assembly.GlobalNamespace, ct))
        {
            var entity = MutatedEntity(type) ?? QueriedEntity(type);
            if (entity is null)
                continue;

            var ns = type.ContainingNamespace?.ToDisplayString();
            if (string.IsNullOrEmpty(ns))
                continue;

            // The first, in a deterministic order: two operations of one entity in different folders
            // is a module whose author already has a harder question than this one.
            var key = entity.ToDisplayString();
            if (!result.TryGetValue(key, out var existing) ||
                string.CompareOrdinal(ns, existing) < 0)
                result[key] = ns!;
        }

        return result;
    }

    /// <summary>The entity of a <c>Mutation&lt;TEntity, …&gt;</c>.</summary>
    private static INamedTypeSymbol? MutatedEntity(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name is not ("Mutation" or "VoidMutation") || current.TypeArguments.Length == 0)
                continue;

            return current.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    /// <summary>The entity of a <c>[Query&lt;TEntity, TResult&gt;]</c>.</summary>
    private static INamedTypeSymbol? QueriedEntity(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType || attrClass.Name != "QueryAttribute")
                continue;
            if (attrClass.TypeArguments.Length == 0)
                continue;

            return attrClass.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return type;
        }

        foreach (var nested in ns.GetNamespaceMembers())
            foreach (var type in EnumerateSourceTypes(nested, ct))
                yield return type;
    }
}
