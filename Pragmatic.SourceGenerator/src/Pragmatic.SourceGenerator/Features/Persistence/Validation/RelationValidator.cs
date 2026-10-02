using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Validation;

/// <summary>
///     Validates the [Relation.*] attributes declared on one entity (PRAG0611-PRAG0617).
/// </summary>
/// <remarks>
///     <para>
///         Every rule here describes generated code that cannot work: two relations collapsing onto one
///         navigation name (the second is silently dropped by RelationGraphBuilder), or an
///         <c>Inverse</c> that does not resolve — EntityConfigurationTemplate binds it by member access
///         (<c>.WithOne(e =&gt; e.Inverse)</c>), so a wrong name becomes a CS1061 inside generated source.
///     </para>
///     <para>
///         Relations are declared, not written: a navigation or a foreign key typed as a property is
///         reported (<c>PRAG0619</c>), and so are EF Core's relational attributes (<c>PRAG0635</c>).
///         Deliberately NOT flagged: a ManyToOne whose parent declares no collection, and relations
///         that cross a boundary — both are correct, intentional modelling in real code.
///     </para>
/// </remarks>
internal static partial class RelationValidator
{
    /// <summary>
    ///     Validates all [Relation.*] attributes on <paramref name="entity" />.
    ///     Returns cache-safe diagnostic models — reporting happens in the source-output stage.
    /// </summary>
    public static ImmutableArray<RelationDiagnosticModel> Validate(INamedTypeSymbol entity)
    {
        var relations = Collect(entity);
        var diagnostics = ImmutableArray.CreateBuilder<RelationDiagnosticModel>();

        // Before the early return: a [PartOf] on an entity with no relation at all is exactly the case
        // the role rules exist for, and it would otherwise be filtered out with the empty list.
        ValidatePartOf(entity, relations, diagnostics);
        ValidateNothingWrittenByHand(entity, diagnostics);

        if (relations.Count == 0)
            return diagnostics.ToImmutable();

        ValidateAmbiguity(entity, relations, diagnostics);
        ValidateInverseNames(entity, relations, diagnostics);
        ValidateDeleteBehaviourOwnership(entity, relations, diagnostics);
        ValidateInverseAgreesWithTheChild(entity, relations, diagnostics);
        ValidateOneToOnePrincipal(entity, relations, diagnostics);
        ValidateInverses(entity, relations, diagnostics);
        ValidateJoinEntityKeys(entity, relations, diagnostics);

        return diagnostics.ToImmutable();
    }

    // =========================================================================
    // Symbol helpers
    // =========================================================================

    private static bool IsCrossBoundary(INamedTypeSymbol entity, INamedTypeSymbol target)
    {
        var ownerBoundary = RelationTransform.FindBelongsToAttribute(entity);
        var targetBoundary = RelationTransform.FindBelongsToAttribute(target);

        if (string.IsNullOrEmpty(ownerBoundary) || string.IsNullOrEmpty(targetBoundary))
            return false;

        return ownerBoundary != targetBoundary;
    }

    private static bool IsEntity(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.OriginalDefinition.Name == "EntityAttribute")
                return true;
        }

        return false;
    }

    /// <summary>Finds a property or field with the given name, walking the base type chain.</summary>
    private static ISymbol? FindNavigationMember(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol or IFieldSymbol)
                    return member;
            }
        }

        return null;
    }

    /// <summary>The entity type a navigation points at — the element type when it is a collection.</summary>
    private static INamedTypeSymbol? NavigationElementType(ISymbol member)
    {
        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };

        if (type is not INamedTypeSymbol named)
            return null;

        // ICollection<T> / List<T> / IEnumerable<T> → T
        if (named.IsGenericType && named.TypeArguments.Length == 1 &&
            named.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T))
        {
            return named.TypeArguments[0] as INamedTypeSymbol;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0] as INamedTypeSymbol;
        }

        return named;
    }

    /// <summary>
    ///     Whether a navigation type is the declaring entity. Inheritance counts in both directions:
    ///     a TPH hierarchy legitimately types the inverse as the base entity.
    /// </summary>
    private static bool PointsBackTo(INamedTypeSymbol? navigationType, INamedTypeSymbol entity)
    {
        if (navigationType is null)
            return true; // Unresolved — never guess.

        for (INamedTypeSymbol? current = navigationType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, entity.OriginalDefinition))
                return true;
        }

        for (INamedTypeSymbol? current = entity; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, navigationType.OriginalDefinition))
                return true;
        }

        return false;
    }

    private static RelationInfo? FindRelationByNavigationName(INamedTypeSymbol type, string navigationName)
    {
        foreach (var (_, info) in RelationDetection.GetRelations(type))
        {
            if (RelationDetection.GetNavigationName(info) == navigationName)
                return info;
        }

        return null;
    }

    /// <summary>The relation type the other side would declare — used to suggest a concrete fix.</summary>
    private static string OppositeRelationType(string relationType) => relationType switch
    {
        "ManyToOne" => "OneToMany",
        "OneToMany" => "ManyToOne",
        "ManyToMany" => "ManyToMany",
        _ => "OneToOne"
    };

    // =========================================================================
    // Collection plumbing
    // =========================================================================

    private static List<Relation> Collect(INamedTypeSymbol entity)
    {
        var relations = new List<Relation>();
        var index = 0;

        foreach (var (attribute, info) in RelationDetection.GetRelations(entity))
        {
            relations.Add(new Relation(
                index++,
                info,
                RelationDetection.GetNavigationName(info),
                LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()),
                attribute.NamedArguments.Any(argument => argument.Key == "OnDelete")));
        }

        return relations;
    }

    private static IEnumerable<List<Relation>> GroupBy(List<Relation> relations, Func<Relation, string> key)
        => relations.GroupBy(key, StringComparer.Ordinal).Select(g => g.ToList());

    private static RelationDiagnosticModel Create(
        RelationDiagnosticKind kind,
        LocationInfo? location,
        params string[] arguments)
        => new()
        {
            Kind = kind,
            Location = location,
            Arguments = arguments.ToImmutableArray()
        };

    /// <summary>One [Relation.*] attribute plus everything validation needs about it.</summary>
    private sealed record Relation(
        int Index,
        RelationInfo Info,
        string NavigationName,
        LocationInfo? Location,
        bool DeclaresOnDelete = false);
}
