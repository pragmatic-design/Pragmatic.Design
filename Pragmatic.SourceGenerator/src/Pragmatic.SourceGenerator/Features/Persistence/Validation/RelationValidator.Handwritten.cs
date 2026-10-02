using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Relations are declared, not written: the members a <c>[Relation.*]</c> generates may not be
///     typed by hand, and EF Core's relational attributes do not stand in for the declaration.
/// </summary>
/// <remarks>
///     <para>
///         A hand-written pair navigation + key, inferred from its shape, would get name and type and
///         every option at its default; a hand-written key alone carries no relationship, and no
///         constraint reaches the schema. Both are a second model that can be read at most halfway.
///         The generator infers nothing from the shape, and this is what tells the author instead of
///         letting the shape work by accident.
///     </para>
///     <para>
///         A key is recognised as one by its name: <c>{Entity}Id</c> where <c>Entity</c> is an
///         <c>[Entity]</c> the validator can resolve — the entity's own namespace first (entities live
///         flat in <c>{Module}.Entities</c>), then its assembly. A key to an entity of another module
///         is not resolved here; declared, it would be a cross-boundary relation, which the generator
///         degrades to the key alone anyway.
///     </para>
/// </remarks>
internal static partial class RelationValidator
{
    private const string EfSchemaNamespace = "System.ComponentModel.DataAnnotations.Schema";

    private static void ValidateNothingWrittenByHand(
        INamedTypeSymbol entity,
        ImmutableArray<RelationDiagnosticModel>.Builder diagnostics)
    {
        foreach (var member in entity.GetMembers())
        {
            if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false, IsImplicitlyDeclared: false } property)
                continue;

            var location = LocationInfo.From(property.Locations.FirstOrDefault());

            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "ForeignKeyAttribute" or "InversePropertyAttribute" } efAttribute
                    && efAttribute.ContainingNamespace?.ToDisplayString() == EfSchemaNamespace)
                {
                    diagnostics.Add(Create(RelationDiagnosticKind.EfCoreRelationAttribute, location,
                        entity.Name, property.Name, efAttribute.Name.Replace("Attribute", "")));
                }
            }

            var navigationTarget = NavigationTargetOf(property.Type);
            if (navigationTarget is not null)
            {
                diagnostics.Add(Create(RelationDiagnosticKind.RelationWrittenByHand, location,
                    entity.Name, property.Name,
                    $"a navigation to '{navigationTarget.Name}' written as a property"));
                continue;
            }

            var keyTarget = ForeignKeyTargetOf(entity, property);
            if (keyTarget is not null)
            {
                diagnostics.Add(Create(RelationDiagnosticKind.RelationWrittenByHand, location,
                    entity.Name, property.Name,
                    $"a foreign key to '{keyTarget.Name}' written as a property"));
            }
        }
    }

    /// <summary>The entity a property's type points at — directly, or as the element of a collection.</summary>
    private static INamedTypeSymbol? NavigationTargetOf(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic
            && generic.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.", StringComparison.Ordinal))
            return generic.TypeArguments[0] is INamedTypeSymbol element && IsEntity(element) ? element : null;

        return type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && IsEntity(named) ? named : null;
    }

    /// <summary>
    ///     The entity a scalar property named <c>{Entity}Id</c> is a key to, when there is one.
    /// </summary>
    private static INamedTypeSymbol? ForeignKeyTargetOf(INamedTypeSymbol entity, IPropertySymbol property)
    {
        if (property.Name == "PersistenceId" || property.Name.Length <= 2
            || !property.Name.EndsWith("Id", StringComparison.Ordinal))
            return null;

        var underlying = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : property.Type;
        if (underlying.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64)
            && underlying.ToDisplayString() != "System.Guid")
            return null;

        var candidate = property.Name.Substring(0, property.Name.Length - 2);
        return ResolveEntityByName(entity, candidate);
    }

    /// <summary>An <c>[Entity]</c> called <paramref name="name" />: in this entity's namespace, else anywhere in its assembly.</summary>
    private static INamedTypeSymbol? ResolveEntityByName(INamedTypeSymbol entity, string name)
    {
        foreach (var type in entity.ContainingNamespace.GetTypeMembers(name))
            if (IsEntity(type))
                return type;

        return FindEntityNamed(entity.ContainingAssembly.GlobalNamespace, name);
    }

    private static INamedTypeSymbol? FindEntityNamed(INamespaceSymbol ns, string name)
    {
        foreach (var type in ns.GetTypeMembers(name))
            if (IsEntity(type))
                return type;

        foreach (var child in ns.GetNamespaceMembers())
        {
            var found = FindEntityNamed(child, name);
            if (found is not null)
                return found;
        }

        return null;
    }
}
