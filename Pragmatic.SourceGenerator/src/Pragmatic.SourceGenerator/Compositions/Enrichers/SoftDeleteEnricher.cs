using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Compositions.Enrichers;

/// <summary>
///     Detects soft-delete configuration from [SoftDelete] on entity type
///     and [Mutation(SoftDelete = true)] explicit flag.
///     Produces a <see cref="SoftDeleteContribution"/> with cascade targets.
/// </summary>
internal static class SoftDeleteEnricher
{
    private const string SoftDeleteAttrFqn = "Pragmatic.Persistence.Entity.SoftDeleteAttribute";
    private const string ISoftDeleteFqn = "Pragmatic.Persistence.Entity.ISoftDelete";
    private const string LookupAttrFqn = "Pragmatic.Persistence.Entity.LookupAttribute";
    private const string EntityAttrNamespace = "Pragmatic.Persistence.Entity";

    private static bool IsLookup(INamedTypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == LookupAttrFqn);

    /// <summary>
    ///     Enriches a mutation with soft-delete information based on entity type and mode.
    /// </summary>
    public static SoftDeleteContribution? Enrich(
        INamedTypeSymbol entityType,
        MutationModeValue mode,
        bool explicitSoftDelete)
    {
        var isRestore = mode == MutationModeValue.Restore;
        var isSoftDelete = explicitSoftDelete ||
            isRestore ||
            (mode == MutationModeValue.Delete && HasSoftDeleteAttribute(entityType));

        if (!isSoftDelete)
            return null;

        var cascade = HasSoftDeleteCascade(entityType);
        var cascadeTargets = cascade
            ? FindCascadeTargets(entityType)
            : ImmutableArray<SoftDeleteCascadeTargetModel>.Empty;

        return new SoftDeleteContribution
        {
            Cascade = cascade,
            CascadeTargets = cascadeTargets,
            IsRestore = isRestore
        };
    }

    public static bool HasSoftDeleteAttribute(INamedTypeSymbol entityType)
    {
        return entityType.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == SoftDeleteAttrFqn);
    }

    /// <summary>
    ///     Whether the entity has the generated nested <c>SoftDeleteFilter</c> — the filter a restore
    ///     lifts by name.
    /// </summary>
    /// <remarks>
    ///     Both attributes, because the class and its DI registration come off the <c>[Entity]</c>
    ///     pipeline and the <c>[SoftDelete]</c> attribute together. A restore on a type that merely
    ///     implements <c>ISoftDelete</c> has no provider filter to lift, and naming one would be a
    ///     reference to a type nobody generates.
    /// </remarks>
    public static bool HasGeneratedSoftDeleteFilter(INamedTypeSymbol entityType)
        => HasSoftDeleteAttribute(entityType) && HasEntityAttribute(entityType);

    private static bool HasEntityAttribute(INamedTypeSymbol entityType)
    {
        return entityType.GetAttributes().Any(a =>
            a.AttributeClass is { } attributeClass &&
            attributeClass.OriginalDefinition.Name == "EntityAttribute" &&
            attributeClass.OriginalDefinition.ContainingNamespace?.ToDisplayString() == EntityAttrNamespace);
    }

    private static bool HasSoftDeleteCascade(INamedTypeSymbol entityType)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != SoftDeleteAttrFqn)
                continue;

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "Cascade", Value.Value: true })
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    ///     Finds navigation properties on the entity whose target type also implements ISoftDelete.
    ///     Supports both source-declared properties and [Relation.*] attribute-declared navigations.
    /// </summary>
    private static ImmutableArray<SoftDeleteCascadeTargetModel> FindCascadeTargets(INamedTypeSymbol entityType)
    {
        var builder = ImmutableArray.CreateBuilder<SoftDeleteCascadeTargetModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Source-declared navigation properties
        foreach (var member in entityType.GetMembers())
        {
            if (member is not IPropertySymbol prop || prop.IsStatic || prop.IsIndexer)
                continue;

            var propType = prop.Type;

            // Collections are recognised by shape (implements IEnumerable<T>), not by a whitelist:
            // a whitelist would let HashSet<T>, Dictionary<TKey,TEntity>, ImmutableArray<T>,
            // IReadOnlySet<T>, T[] and custom collections fall through unrecognised, so the cascade
            // would silently skip those navigations and leave the child rows alive.
            var elementTypes = CollectionTypeHelper.GetElementTypes(propType);
            if (elementTypes.Count > 0)
            {
                if (elementTypes.Any(e => e is INamedTypeSymbol named && ImplementsISoftDelete(named)) &&
                    seen.Add(prop.Name))
                {
                    builder.Add(new SoftDeleteCascadeTargetModel
                    {
                        PropertyName = prop.Name,
                        IsCollection = true
                    });
                }
            }
            else if (propType is INamedTypeSymbol { IsValueType: false } refType)
            {
                if (ImplementsISoftDelete(refType) && seen.Add(prop.Name))
                {
                    builder.Add(new SoftDeleteCascadeTargetModel
                    {
                        PropertyName = prop.Name,
                        IsCollection = false
                    });
                }
            }
        }

        // 2. [Relation.*] attribute-declared navigations
        foreach (var attr in entityType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var (relationType, targetType) = ParseRelationAttribute(attrClass);
            if (targetType is null || !ImplementsISoftDelete(targetType))
                continue;

            // A cascade follows ownership, and a join table is not ownership: the other side of a
            // many-to-many belongs to everyone who points at it. Cascading here would make deleting
            // one property soft-delete the "Wi-Fi" amenity itself, hiding it from every other property
            // that has it.
            if (relationType is "ManyToMany")
                continue;

            // The same rule from the other end: a ManyToOne is the child's pointer to its parent, and
            // deleting a child does not delete what it points at. Cascading here would make deleting
            // a property soft-delete the category it belongs to.
            if (relationType is "ManyToOne")
                continue;

            // A [Lookup] is never a part of anything, and its reference member is [NotMapped] —
            // including it by name is an EF error at runtime, not a no-op.
            if (IsLookup(targetType))
                continue;

            string? navName = null;
            if (attrClass.Name == "WithNavigation" && attr.ConstructorArguments.Length > 0)
                navName = attr.ConstructorArguments[0].Value as string;

            var propertyName = navName ?? (relationType is "OneToOne" or "ManyToOne"
                ? targetType.Name
                : targetType.Name + "s");

            if (seen.Add(propertyName))
            {
                builder.Add(new SoftDeleteCascadeTargetModel
                {
                    PropertyName = propertyName,
                    IsCollection = relationType is "OneToMany" or "ManyToMany"
                });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Delegates to the shared reader rather than keeping a copy of the same shape.</summary>
    private static (string? RelationType, INamedTypeSymbol? TargetType) ParseRelationAttribute(
        INamedTypeSymbol attrClass)
        => RelationAttributeReader.Read(attrClass);

    /// <summary>
    ///     Whether the cascade may reach <paramref name="type" /> — declared either way.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The interface alone is not enough, and testing only for it was a defect with no symptom:
    ///         an entity that declares <c>[SoftDelete]</c> receives <c>ISoftDelete</c> from a partial
    ///         <b>this generator emits</b>, and a generator cannot see another's output in the same
    ///         compilation. So <c>AllInterfaces</c> never contained it, <c>FindCascadeTargets</c>
    ///         returned nothing, and <c>[SoftDelete(Cascade = true)]</c> did nothing at all for every
    ///         child declared the documented way — only a hand-written <c>: ISoftDelete</c> ever worked.
    ///     </para>
    ///     <para>
    ///         Found by a consumer application: retiring a term left every mention of it live and
    ///         queryable, with the attribute in place and the generated <c>DeleteEntity</c> carrying no
    ///         cascade at all.
    ///     </para>
    /// </remarks>
    private static bool ImplementsISoftDelete(INamedTypeSymbol type)
    {
        return TraitDetector.Detect(type).IsSoftDelete
               || type.AllInterfaces.Any(i => i.ToDisplayString() == ISoftDeleteFqn);
    }
}
