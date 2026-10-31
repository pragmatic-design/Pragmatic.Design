using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Owned types: <c>[Owned]</c>, the repository's <c>[ValueObject]</c> records of several members (read by name), or an identity record (a
///     <c>Pragmatic.Identity.IdentityRecord</c>, such as <c>LocalIdentity</c>), flattened into the owner's table with their base classes' members;
///     one without a parameterless constructor (a value object's) is built through the constructor whose parameters are its members, as EF Core binds them.
/// </summary>
internal sealed partial class EntityModel
{
    public static bool IsOwned(ITypeSymbol type)
        => Unwrap(type) is INamedTypeSymbol named
           && (named.GetAttributes().Any(a => a.AttributeClass?.Name is "OwnedAttribute" or "ValueObjectAttribute") || IsIdentityRecord(named));

    /// <summary>
    ///     An identity record carries no attribute: EF Core's branch owns it because it derives from <c>IdentityRecord</c>
    ///     (<c>EntityTransform.CollectNavigations</c>), so the Sql engine reads it by the same rule.
    /// </summary>
    private static bool IsIdentityRecord(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            if (current.Name == "IdentityRecord" && current.ContainingNamespace?.ToDisplayString() == "Pragmatic.Identity")
                return true;
        return false;
    }

    /// <summary>
    ///     The members an owned type maps: its public read-write instance properties and its base classes', the derived declaration winning a
    ///     hidden name. An identity record's key and audit stamps are on its base class.
    /// </summary>
    public static IEnumerable<IPropertySymbol> OwnedMembers(INamedTypeSymbol ownedType)
        => Core.OwnedMemberWalk.PropertiesIncludingInherited(ownedType).Where(p => p.GetMethod is not null && p.SetMethod is not null);

    /// <summary>An owned property, unless a converter stores it as one column (a single-value object: <c>[Convert&lt;TConverter&gt;]</c>).</summary>
    public static bool IsOwnedProperty(IPropertySymbol property)
        => IsOwned(property.Type) && !property.GetAttributes().Any(a => a.AttributeClass is { Name: "ConvertAttribute", TypeArguments.Length: 1 });

    /// <summary>
    ///     The members a constructor takes, in its order, for an owned type without a public parameterless constructor: the public constructor with the most
    ///     parameters that all name a member (ignoring case). Empty when the type has a parameterless one (built with an initializer); null when none fits.
    /// </summary>
    public static List<string>? ConstructorMembers(INamedTypeSymbol type, IReadOnlyList<ColumnModel> members)
    {
        var constructors = type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        if (constructors.Any(c => c.Parameters.Length == 0))
            return [];
        foreach (var constructor in constructors.OrderByDescending(c => c.Parameters.Length))
        {
            var names = constructor.Parameters.Select(p => members.FirstOrDefault(m => string.Equals(m.Member, p.Name, StringComparison.OrdinalIgnoreCase))?.Member).ToList();
            if (names.Count > 0 && names.All(n => n is not null))
                return names!;
        }

        return null;
    }

    /// <summary>The C# that builds an owned object from its members' reads: <c>new T { A = …, B = … }</c>, or <c>new T(…) { the others }</c> through its constructor.</summary>
    public static string ConstructOwned(OwnedModel owned, Func<int, string> read)
    {
        if (owned.Constructor.Count == 0)
            return $"new {owned.TypeName} {{ {string.Join(", ", owned.Columns.Select((c, m) => $"{c.Member} = {read(m)}"))} }}";
        var arguments = owned.Constructor.Select(name => read(owned.Columns.FindIndex(c => c.Member == name)));
        var others = owned.Columns.Select((c, m) => (c, m)).Where(x => !owned.Constructor.Contains(x.c.Member)).Select(x => $"{x.c.Member} = {read(x.m)}").ToList();
        return $"new {owned.TypeName}({string.Join(", ", arguments)}){(others.Count == 0 ? "" : $" {{ {string.Join(", ", others)} }}")}";
    }
}
