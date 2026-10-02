using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Works out how a collection of DTOs is written back, and by what its elements are matched.
/// </summary>
/// <remarks>
///     <para>
///         The strategy is derived from what the DTO already says it is, so the common case needs no
///         attribute. What cannot be derived from any amount of schema metadata is what the
///         <i>absence</i> of an element means — the DTO sends <c>[a, b]</c>, the entity holds
///         <c>[a, b, c]</c>, and "remove c" and "I am only telling you about a and b" are equally
///         consistent with the same relation. That is a fact about the API contract, and the developer
///         has already stated it by choosing the kind of DTO.
///     </para>
/// </remarks>
internal static class CollectionWriteAnalyzer
{
    private const string MappingAttributes = "Pragmatic.Mapping.Attributes";
    private const string PatchAttributes = "Pragmatic.Persistence.Patch";

    /// <summary>
    ///     The write model for one collection-of-DTOs property, or null when the property is not one.
    /// </summary>
    /// <param name="dtoProperty">The DTO property holding the collection.</param>
    /// <param name="elementDtoType">The element DTO's symbol.</param>
    /// <param name="entityCollectionProperty">The matching collection on the entity, if there is one.</param>
    public static CollectionWriteModel? Analyze(
        IPropertySymbol dtoProperty,
        INamedTypeSymbol? elementDtoType,
        IPropertySymbol? entityCollectionProperty)
        => Analyze(dtoProperty, elementDtoType, ElementTypeOf(entityCollectionProperty?.Type));

    /// <summary>
    ///     The same, given the child entity directly.
    /// </summary>
    /// <remarks>
    ///     A navigation the persistence generator writes is not a property symbol during this pass, so
    ///     a caller that resolved one has the child's type and nothing to pass as a property.
    /// </remarks>
    /// <param name="dtoProperty">The DTO property holding the collection.</param>
    /// <param name="elementDtoType">The element DTO's symbol.</param>
    /// <param name="childEntityType">The entity on the other side of the navigation.</param>
    public static CollectionWriteModel? Analyze(
        IPropertySymbol dtoProperty,
        INamedTypeSymbol? elementDtoType,
        ITypeSymbol? childEntityType)
    {
        if (elementDtoType is null)
            return null;

        var declared = ReadDeclaredStrategy(dtoProperty);
        var strategy = declared ?? DeriveStrategy(dtoProperty.ContainingType);

        // Ignore writes nothing, so it needs no key and reports no problem: a read-only collection is
        // a legitimate shape, not an incomplete one.
        if (strategy == "Ignore")
            return new CollectionWriteModel { Strategy = strategy, IsExplicit = declared is not null };

        // Replace rebuilds the collection from scratch, so it matches nothing and needs no key either.
        if (strategy == "Replace")
            return new CollectionWriteModel { Strategy = strategy, IsExplicit = declared is not null };

        var (dtoKey, entityKey, problem) = ResolveKey(elementDtoType, childEntityType);

        return new CollectionWriteModel
        {
            Strategy = strategy,
            IsExplicit = declared is not null,
            DtoKeyProperty = dtoKey,
            EntityKeyProperty = entityKey,
            KeyProblem = problem,
        };
    }

    /// <summary>
    ///     <c>[CollectionStrategy(CollectionStrategy.X)]</c> on the property, or the given default.
    /// </summary>
    /// <remarks>
    ///     Exposed for <see cref="LinkIdsAnalyzer" />: a list of keys is still a collection, and the
    ///     four strategies mean there what they mean here. Reading the attribute twice, in two
    ///     places, would be a second copy of the enum's positional contract.
    /// </remarks>
    internal static string ReadDeclaredStrategyOrDefault(IPropertySymbol dtoProperty, string fallback)
        => ReadDeclaredStrategy(dtoProperty) ?? fallback;

    /// <summary>
    ///     <c>[CollectionStrategy(CollectionStrategy.X)]</c> on the property, if present.
    /// </summary>
    private static string? ReadDeclaredStrategy(IPropertySymbol dtoProperty)
    {
        foreach (var attribute in dtoProperty.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "CollectionStrategyAttribute" } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != MappingAttributes
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not int value)
                continue;

            return value switch
            {
                0 => "Sync",
                1 => "AddOnly",
                2 => "Replace",
                3 => "Ignore",
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    ///     The strategy the DTO's own kind implies.
    /// </summary>
    /// <remarks>
    ///     A <c>[Patch&lt;T&gt;]</c> exists to carry a delta — that is the whole point of the type — so
    ///     a collection on it adds and updates and removes nothing. Anything else is a full
    ///     representation, and a full representation that omits a child is saying the child is gone.
    /// </remarks>
    private static string DeriveStrategy(INamedTypeSymbol dtoType)
    {
        foreach (var attribute in dtoType.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "PatchAttribute" } patch
                && patch.ContainingNamespace?.ToDisplayString() == PatchAttributes)
                return "AddOnly";
        }

        return "Sync";
    }

    /// <summary>
    ///     What the two sides are matched by: the element DTO's own id first, then the child entity's
    ///     domain key.
    /// </summary>
    /// <remarks>
    ///     The id comes first because an element that carries one is naming a row that exists. The
    ///     logic key is the fallback for an element the caller is describing rather than addressing —
    ///     a line identified by its product code, not by a database id it has never seen.
    /// </remarks>
    private static (string? DtoKey, string? EntityKey, CollectionKeyProblem Problem) ResolveKey(
        INamedTypeSymbol elementDto, ITypeSymbol? elementEntity)
    {
        if (elementEntity is not INamedTypeSymbol entity)
            return (null, null, CollectionKeyProblem.NoKey);

        if (HasProperty(elementDto, "Id") && HasProperty(entity, "Id"))
            return ("Id", "Id", CollectionKeyProblem.None);

        var logicKey = LogicKeyOf(entity);
        if (logicKey is null)
            return (null, null, CollectionKeyProblem.NoKey);

        return HasProperty(elementDto, logicKey)
            ? (logicKey, logicKey, CollectionKeyProblem.None)
            : (null, logicKey, CollectionKeyProblem.KeyNotOnBothSides);
    }

    /// <summary>The single <c>[LogicKey]</c> property, or null when there is none or more than one.</summary>
    /// <remarks>
    ///     More than one is not an error here, it is simply not usable as a match key by a helper that
    ///     takes one selector. A composite domain key on a child collection is a case to reopen when
    ///     something needs it.
    /// </remarks>
    private static string? LogicKeyOf(INamedTypeSymbol entity)
    {
        string? found = null;

        for (var current = entity; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol property)
                    continue;

                foreach (var attribute in property.GetAttributes())
                {
                    if (attribute.AttributeClass is not { Name: "LogicKeyAttribute" } key
                        || key.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity")
                        continue;

                    if (found is not null)
                        return null;

                    found = property.Name;
                }
            }
        }

        return found;
    }

    private static bool HasProperty(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol { IsStatic: false })
                    return true;
            }
        }

        // The entity's Id is written by this generator from [Entity], so it is not on the symbol
        // during this pass. Asking for it directly would answer "no" for every entity there is.
        return name == "Id" && HasEntityAttribute(type);
    }

    private static bool HasEntityAttribute(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "EntityAttribute")
                    return true;
            }
        }

        return false;
    }

    /// <summary>The element type of a collection, or null when the type is not one.</summary>
    private static ITypeSymbol? ElementTypeOf(ITypeSymbol? collectionType)
    {
        if (collectionType is IArrayTypeSymbol array)
            return array.ElementType;

        if (collectionType is not INamedTypeSymbol { IsGenericType: true } named)
            return null;

        return named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null;
    }
}
