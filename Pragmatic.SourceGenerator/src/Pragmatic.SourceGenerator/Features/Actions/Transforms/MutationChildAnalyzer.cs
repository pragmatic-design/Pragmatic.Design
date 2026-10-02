using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Works out whether a mutation property carries children of the aggregate, and whether it is
///     allowed to write them.
/// </summary>
/// <remarks>
///     <para>
///         Before this, a mutation could not carry children at all: <c>MatchProperties</c> paired the
///         property with the entity's navigation of the same name and the auto-map emitted
///         <c>entity.SetLines(this.Lines)</c> — a <c>List&lt;LineDto&gt;</c> assigned to an
///         <c>ICollection&lt;Line&gt;</c>, which is a compile error inside a file the author cannot
///         open. The reference application says as much by hand: <c>CreateDraftInvoiceMutation</c> adds
///         its line item in <c>ApplyAsync</c> because there was no other way.
///     </para>
///     <para>
///         Whether the parent may write the child is not derivable from the relation — <c>Invoice</c>
///         and <c>Property</c> declare the same <c>[Relation.OneToMany]</c> over children of opposite
///         natures. The child states it, with <c>[PartOf&lt;TParent&gt;]</c>, and without that
///         statement the answer is no.
///     </para>
/// </remarks>
internal static class MutationChildAnalyzer
{
    private const string MappingAttributes = "Pragmatic.Mapping.Attributes";
    private const string PatchAttributes = "Pragmatic.Persistence.Patch";
    private const string EntityAttributes = "Pragmatic.Persistence.Entity";
    private const string MutationAttributes = "Pragmatic.Actions.Mutation";

    /// <summary>
    ///     The navigation on the entity a child is written to, however it got there.
    /// </summary>
    /// <param name="Name">Its name on the entity.</param>
    /// <param name="ElementType">
    ///     The entity on the other side — the element type for a collection. It is what the key is
    ///     resolved against, so a navigation without it can be written but not matched.
    /// </param>
    /// <param name="HasNonPublicSetter">Whether it is written through the generated setter.</param>
    /// <remarks>
    ///     A record rather than the property symbol, because most navigations are not symbols at all
    ///     during this pass: a relation is declared once and both sides get their member from another
    ///     generator's output, invisible in the same compilation.
    /// </remarks>
    internal readonly record struct EntityNavigation(
        string Name,
        ITypeSymbol ElementType,
        bool HasNonPublicSetter);

    /// <summary>
    ///     The child a mutation property carries, or null when it carries a value.
    /// </summary>
    /// <param name="mutationProperty">The property on the mutation.</param>
    /// <param name="target">The matching navigation on the entity, if there is one.</param>
    /// <param name="parentEntity">The entity the mutation operates on.</param>
    public static MutationChildModel? Analyze(
        IPropertySymbol mutationProperty,
        EntityNavigation? target,
        INamedTypeSymbol parentEntity)
    {
        var (isCollection, dtoType) = UnwrapChildDto(mutationProperty.Type);
        if (dtoType is null)
            return null;

        // A child that is itself a [Mutation] is the shape this framework wants: it carries its own
        // validation and permissions, so writing through it is not a way around them. It needs no
        // mapping attribute — `ApplyToEntity` is virtual on Mutation<TEntity>, so generated code can
        // name it before any generator has written the override, which is exactly the constraint that
        // forced the attribute-reading design for DTO children.
        var childMutationEntity = MutationEntityOf(dtoType);
        var mappedEntity = childMutationEntity ?? MappedEntityOf(dtoType);
        if (mappedEntity is null)
            return null;

        var write = isCollection
            ? CollectionWriteAnalyzer.Analyze(mutationProperty, dtoType, target?.ElementType)
            : null;

        return new MutationChildModel
        {
            PropertyName = mutationProperty.Name,
            TargetPropertyName = target?.Name ?? mutationProperty.Name,
            ChildTypeName = mappedEntity.Name,
            ChildDtoTypeName = dtoType.Name,
            ChildDtoFullTypeName = dtoType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat),
            DeclaredParentTypeName = PartOfParentOf(mappedEntity),
            IsCollection = isCollection,
            Collection = write,
            // The child's own rules, checked by the aggregate's invoker: this is the only path a
            // [PartOf] child is written on, so it is the only path they can be checked on.
            Invariants = MutationTransform.ParseInvariants(mappedEntity, parentEntity.ContainingAssembly),
            IsChildMutation = childMutationEntity is not null,
            ChildEntityFullTypeName = mappedEntity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ChildEntityHasParameterlessFactory =
                Core.TraitPropertyResolver.WillHaveParameterlessFactory(mappedEntity),
            // A child mutation can always do both: `new TEntity()` builds one and `ApplyToEntity`
            // fills it, which is what the invoker itself does for the root.
            CanCreate = childMutationEntity is not null
                || HasAttribute(dtoType, "MapToAttribute", MappingAttributes),
            CanPatch = childMutationEntity is null
                && HasAttribute(dtoType, "PatchAttribute", PatchAttributes),
            EntityHasPrivateSetter = target?.HasNonPublicSetter ?? false,
            EntityPropertyExists = target is not null,
            NavigationElementTypeName = target?.ElementType.Name,
            ReferenceStrategy = ReferenceWriteAnalyzer.ReadStrategy(mutationProperty),
            ParentTypeName = parentEntity.Name,
            ParentBoundaryName =
                Persistence.Transforms.EntityTransform.GetBoundaryInfo(parentEntity).Name,
            ChildBoundaryName =
                Persistence.Transforms.EntityTransform.GetBoundaryInfo(mappedEntity).Name,
        };
    }

    /// <summary>The element DTO of a collection property, or the DTO itself for a single one.</summary>
    private static (bool IsCollection, INamedTypeSymbol? Dto) UnwrapChildDto(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);

        if (unwrapped is IArrayTypeSymbol array)
            return (true, array.ElementType as INamedTypeSymbol);

        if (unwrapped is not INamedTypeSymbol named)
            return (false, null);

        // A collection is only a collection here when its element could be a DTO. One type argument
        // and an ICollection<T> among the interfaces is what the write path can actually merge.
        if (named is { IsGenericType: true, TypeArguments.Length: 1 }
            && named.AllInterfaces.Concat([named]).Any(i =>
                i.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.ICollection")
                || i.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable")))
            return (true, named.TypeArguments[0] as INamedTypeSymbol);

        return (false, named);
    }

    /// <summary>The entity a DTO maps to, from <c>[MapTo&lt;T&gt;]</c> or <c>[Patch&lt;T&gt;]</c>.</summary>
    /// <summary>
    ///     The entity a child <c>[Mutation]</c> writes — the type argument of its
    ///     <c>Mutation&lt;TEntity&gt;</c> base — or null when the type is not a mutation.
    /// </summary>
    /// <remarks>
    ///     Read from the base rather than from an attribute, because a mutation declares its target
    ///     by what it inherits. <c>[Mutation]</c> itself is not generic.
    /// </remarks>
    private static INamedTypeSymbol? MutationEntityOf(INamedTypeSymbol type)
    {
        var isMutation = type.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "MutationAttribute" } declaration
            && declaration.ContainingNamespace?.ToDisplayString() == MutationAttributes);

        if (!isMutation)
            return null;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "Mutation", IsGenericType: true, TypeArguments.Length: 1 }
                && current.ContainingNamespace?.ToDisplayString() == MutationAttributes)
                return current.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static INamedTypeSymbol? MappedEntityOf(INamedTypeSymbol dto)
    {
        foreach (var attribute in dto.GetAttributes())
        {
            var declaration = attribute.AttributeClass;
            if (declaration is not { IsGenericType: true, TypeArguments.Length: 1 })
                continue;

            var ns = declaration.ContainingNamespace?.ToDisplayString();
            var isMapTo = declaration.Name == "MapToAttribute" && ns == MappingAttributes;
            var isPatch = declaration.Name == "PatchAttribute" && ns == PatchAttributes;

            if (isMapTo || isPatch)
                return declaration.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    /// <summary>The parent named by <c>[PartOf&lt;TParent&gt;]</c> on the entity, if any.</summary>
    public static string? PartOfParentOf(INamedTypeSymbol entity)
    {
        for (var current = entity; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "PartOfAttribute", TypeArguments.Length: 1 } declaration
                    && declaration.ContainingNamespace?.ToDisplayString() == EntityAttributes)
                    return declaration.TypeArguments[0].Name;
            }
        }

        return null;
    }

    /// <summary>
    ///     Whether <c>[PartOf]</c> on this entity claims to be the only way in.
    /// </summary>
    /// <remarks>
    ///     True also when the entity declares no <c>[PartOf]</c> at all: the question only means
    ///     something for one that does, and the caller asks it after establishing that.
    /// </remarks>
    public static bool PartOfIsExclusive(INamedTypeSymbol entity)
    {
        for (var current = entity; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is not { Name: "PartOfAttribute", TypeArguments.Length: 1 } declaration
                    || declaration.ContainingNamespace?.ToDisplayString() != EntityAttributes)
                    continue;

                foreach (var named in attribute.NamedArguments)
                    if (named.Key == "Exclusive" && named.Value.Value is false)
                        return false;

                return true;
            }
        }

        return true;
    }

    private static bool HasAttribute(INamedTypeSymbol type, string name, string containingNamespace)
    {
        return type.GetAttributes().Any(a =>
            a.AttributeClass?.Name == name
            && a.AttributeClass.ContainingNamespace?.ToDisplayString() == containingNamespace);
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : type;
    }
}
