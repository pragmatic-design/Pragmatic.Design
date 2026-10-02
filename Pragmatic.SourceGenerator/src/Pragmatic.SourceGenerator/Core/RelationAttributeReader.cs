using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads a <c>[Relation.*]</c> attribute off a symbol.
/// </summary>
/// <remarks>
///     The shape is not obvious and was open-coded in three places: the attribute classes are
///     <c>Relation.ManyToOne&lt;T&gt;</c> and friends — nested types with no <c>Attribute</c> suffix —
///     and <c>Relation.ManyToOne&lt;T&gt;.WithNavigation</c> nests one level deeper again. A copy that
///     compared against <c>"ManyToOneAttribute"</c> matched nothing and failed silently.
/// </remarks>
internal static class RelationAttributeReader
{
    /// <summary>The nesting type every relation attribute lives in.</summary>
    private const string ContainerName = "Relation";

    /// <summary>The deeper nesting used by the navigation-naming variants.</summary>
    private const string NavigationVariantName = "WithNavigation";

    /// <summary>
    ///     The relation kind (<c>ManyToOne</c>, <c>OneToMany</c>, <c>OneToOne</c>, <c>ManyToMany</c>)
    ///     and the related type, or <c>(null, null)</c> when the attribute is not a relation.
    /// </summary>
    public static (string? Kind, INamedTypeSymbol? Target) Read(INamedTypeSymbol? attributeClass)
    {
        var current = attributeClass;

        if (current?.Name == NavigationVariantName)
            current = current.ContainingType;

        if (current?.ContainingType?.Name != ContainerName)
            return (null, null);

        var target = current.TypeArguments.Length > 0
            ? current.TypeArguments[0] as INamedTypeSymbol
            : null;

        return (current.Name, target);
    }

    /// <summary>
    ///     Whether the relation puts the foreign key on the declaring entity rather than the other
    ///     side. <c>OneToMany</c> puts it on the child and <c>ManyToMany</c> in a join table.
    /// </summary>
    public static bool OwnsForeignKey(string? kind) => kind is "ManyToOne" or "OneToOne";

    /// <summary>
    ///     Whether the relation puts a <em>reference</em> navigation on the declaring entity — the
    ///     same side that owns the foreign key.
    /// </summary>
    public static bool OwnsReferenceNavigation(string? kind) => OwnsForeignKey(kind);

    /// <summary>
    ///     Whether the relation puts a <em>collection</em> navigation on the declaring entity.
    /// </summary>
    public static bool OwnsCollectionNavigation(string? kind) => kind is "OneToMany" or "ManyToMany";
}
