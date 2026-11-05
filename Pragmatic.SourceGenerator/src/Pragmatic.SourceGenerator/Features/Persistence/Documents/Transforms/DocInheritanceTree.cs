using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A Pragmatic entity of a hierarchy (<c>[Inheritance]</c> on its root) as documents keep it (#539): every type in the root's collection, a
///     discriminator field naming each document's type as the Sql engine's column does. The root's and an intermediate type's reads build the
///     type the discriminator names; a derived type's reads match its own name and the names below it.
/// </summary>
/// <remarks>
///     ASSUMPTION: every strategy is one collection. Table per type and per concrete type are how a relational database lays the hierarchy out; a
///     document holds the whole entity whatever its type, so there is nothing to split.
/// </remarks>
internal sealed class DocInheritanceTree
{
    private DocInheritanceTree(INamedTypeSymbol type, INamedTypeSymbol root, string discriminator, List<INamedTypeSymbol> below)
    {
        Type = type;
        Root = root;
        Discriminator = discriminator;
        Below = below;
    }

    public INamedTypeSymbol Type { get; }

    /// <summary>The type with <c>[Inheritance]</c>: its collection is the hierarchy's.</summary>
    public INamedTypeSymbol Root { get; }

    /// <summary>The discriminator field's name: the root's <c>DiscriminatorColumn</c>, <c>Discriminator</c> when it names none.</summary>
    public string Discriminator { get; }

    /// <summary>The entities below this type, at any depth, the deepest first: the first that matches is the most derived one.</summary>
    public List<INamedTypeSymbol> Below { get; }

    public bool IsRoot => SymbolEqualityComparer.Default.Equals(Type, Root);

    /// <summary>The discriminator's value of a type: its name, as the Sql engine writes it.</summary>
    public static string NameOf(INamedTypeSymbol type) => type.Name;

    /// <summary>The names a derived type's reads match: its own (unless it is abstract) and those of the types below it.</summary>
    public List<string> Accepted => (Type.IsAbstract ? [] : new[] { NameOf(Type) }).Concat(Below.Select(NameOf)).ToList();

    /// <summary>The hierarchy <paramref name="type" /> is in; null outside one.</summary>
    public static DocInheritanceTree? Of(INamedTypeSymbol type)
    {
        if (EntityModel.HierarchyRootOf(type) is not { } root || !DocModel.IsPragmatic(root))
            return null;
        var attribute = root.GetAttributes().First(a => a.AttributeClass?.Name == "InheritanceAttribute");
        var discriminator = attribute.NamedArguments.FirstOrDefault(n => n.Key == "DiscriminatorColumn").Value.Value as string
                            ?? attribute.NamedArguments.FirstOrDefault(n => n.Key == "Discriminator").Value.Value as string ?? "Discriminator";
        var below = EntityModel.DerivedTypesOf(type)
            .OrderByDescending(d => EntityModel.Depth(d, type)).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        return new DocInheritanceTree(type, root, discriminator, below);
    }
}
