using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform for extracting property mappings from mutation DTOs.
///     Reads [MapIgnore], [MapProperty], [MapConverter] attributes from Pragmatic.Mapping.
/// </summary>
internal static class MutationPropertyTransform
{
    // Attribute names from Pragmatic.Mapping
    private const string MapIgnoreAttribute = "Pragmatic.Mapping.Attributes.MapIgnoreAttribute";
    private const string MapPropertyAttribute = "Pragmatic.Mapping.Attributes.MapPropertyAttribute";
    private const string MapConverterAttribute = "Pragmatic.Mapping.Attributes.MapConverterAttribute`1";

    /// <summary>
    ///     Extracts property mappings from a mutation DTO type.
    /// </summary>
    public static ImmutableArray<MutationPropertyModel> ExtractProperties(
        INamedTypeSymbol dtoType,
        INamedTypeSymbol entityType)
    {
        var builder = ImmutableArray.CreateBuilder<MutationPropertyModel>();

        // Get entity properties for matching
        var entityProperties = GetAllProperties(entityType)
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        // ⚠️ And the navigations another generator will add. A relation is declared once, on one
        // side, and both ends get their member from it — so one named by
        // [Relation.OneToMany<T>.WithNavigation("Lines")] is not a symbol during this pass. Matching
        // only against source would find nothing, the child would not be recognised as one, and the
        // patch would publish an EMPTY WrittenNavigations: the caller would load nothing and the merge
        // would add every row it was sent. A generator test whose fixture declares the navigation by
        // hand does not exercise this; the E2E in conformance, where relations are declared the way
        // an application declares them, does.
        var predictedNavigations = Core.TraitPropertyResolver.GetRelationNavigations(entityType);

        // Process each DTO property
        foreach (var dtoProp in GetAllProperties(dtoType))
        {
            // Skip static properties
            if (dtoProp.IsStatic)
                continue;

            // Check for [MapIgnore]
            // A patch only ever writes, so an ignore that covers writing excludes it.
            var isIgnored = Mapping.Analysis.AttributeAnalyzer.IsIgnoredFor(dtoProp, writing: true);

            // Check for [MapProperty]
            var mapPropertyAttr = GetAttribute(dtoProp, MapPropertyAttribute);
            var targetPropertyName = GetTargetPropertyName(mapPropertyAttr) ?? dtoProp.Name;

            // Check for [MapConverter<T>]
            var converterType = GetConverterType(dtoProp);

            // Check if entity has a settable property (read-only computed properties are excluded)
            entityProperties.TryGetValue(targetPropertyName, out var entityProp);

            // The navigation another generator will add, when the source has none by that name.
            ITypeSymbol? predictedNavigation = null;
            if (entityProp is null)
            {
                foreach (var navigation in predictedNavigations)
                {
                    if (!string.Equals(navigation.Name, targetPropertyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    predictedNavigation = navigation.TypeSymbol;
                    break;
                }
            }

            // ⚠️ A predicted navigation exists too. Without this the child was filtered out of
            // ApplyPatch's body — `EntityPropertyExists` gates it — so a patch carrying children
            // compiled, answered 200 and wrote none of them. The published list was the first half of
            // the same defect; this is the second, and only the E2E saw either.
            var entityPropertyExists =
                (entityProp is not null && entityProp.SetMethod is not null)
                || predictedNavigation is not null;
            // Check if setter is non-public (requires generated SetXxx method)
            // ⚠️ On the source property, not on "it exists": a predicted navigation exists and has no
            // symbol, so the null-forgiving pair below threw once the two stopped meaning the same.
            var entityHasPrivateSetter = entityProp?.SetMethod is { } setter
                && setter.DeclaredAccessibility != Accessibility.Public;
            var entityPropertyType = entityProp?.Type.ToDisplayString();

            // Analyze nested mutation / collection
            var (isNestedMutation, nestedMutationType) = AnalyzeNestedMutation(dtoProp.Type);
            var (isCollection, collectionKind, isElementMutation, elementMutationType, elementEntityType) =
                AnalyzeCollection(dtoProp.Type, entityProp?.Type);

            // Determine if the underlying type is a value type
            var underlyingType = UnwrapNullable(dtoProp.Type);
            var isValueType = underlyingType.IsValueType;

            // What the related DTO — a collection element, or the nested DTO itself — is able to do
            // with an entity. A patch that carries children can only write them through the members
            // those attributes produce, so the template has to be told which ones exist.
            var relatedType = isCollection
                ? ElementSymbolOf(dtoProp.Type)
                : UnwrapNullable(dtoProp.Type) as INamedTypeSymbol;

            var relatedCanCreate = relatedType is not null && HasMapToAttribute(relatedType);
            var relatedCanPatch = relatedType is not null && HasPatchAttribute(relatedType);

            var collectionWrite = isCollection && relatedType is not null && (relatedCanCreate || relatedCanPatch)
                ? entityProp is not null
                    ? CollectionWriteAnalyzer.Analyze(dtoProp, relatedType, entityProp)
                    : CollectionWriteAnalyzer.Analyze(dtoProp, relatedType, predictedNavigation)
                : null;

            builder.Add(new MutationPropertyModel
            {
                CollectionWrite = collectionWrite,
                RelatedCanCreate = relatedCanCreate,
                RelatedCanUpdate = relatedCanCreate,
                RelatedCanPatch = relatedCanPatch,
                RelatedDtoType = relatedType?.ToDisplayString(),
                RelatedDtoFullTypeName = relatedType?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat),
                ReferenceStrategy = ReferenceWriteAnalyzer.ReadStrategy(dtoProp),
                RelatedIsUserType = relatedType is { SpecialType: SpecialType.None }
                    && relatedType.Locations.Any(l => l.IsInSource),
                PropertyName = dtoProp.Name,
                PropertyType = dtoProp.Type.ToDisplayString(),
                // With the `?` of a nullable reference type: the converter hands this to JsonTypeInfo<T>,
                // and JsonTypeInfo<string> is not a JsonTypeInfo<string?> to the compiler (CS8620).
                PropertyFullTypeName = dtoProp.Type.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),
                IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                HasSetter = dtoProp.SetMethod is not null,
                IsRequired = dtoProp.IsRequired,
                IsValueType = isValueType,
                IsIgnored = isIgnored,
                TargetPropertyName = targetPropertyName,
                ConverterType = converterType,
                IsNestedMutation = isNestedMutation,
                NestedMutationType = nestedMutationType,
                IsCollection = isCollection,
                CollectionKind = collectionKind,
                IsElementMutation = isElementMutation,
                ElementMutationType = elementMutationType,
                ElementEntityType = elementEntityType,
                EntityHasPrivateSetter = entityHasPrivateSetter,
                EntityPropertyExists = entityPropertyExists,
                EntityPropertyType = entityPropertyType
            });
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol { IsIndexer: false } prop)
                    yield return prop;

            current = current.BaseType;
        }
    }

    private static bool HasAttribute(IPropertySymbol prop, string attributeName)
    {
        return prop.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString().StartsWith(attributeName) == true);
    }

    private static AttributeData? GetAttribute(IPropertySymbol prop, string attributeName)
    {
        return prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString().StartsWith(attributeName) == true);
    }

    private static string? GetTargetPropertyName(AttributeData? attr)
    {
        if (attr is null)
            return null;

        // Look for "From" named argument or first constructor argument
        foreach (var arg in attr.NamedArguments)
            if (arg is { Key: "From", Value.Value: string fromValue })
                return fromValue;

        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string ctorValue)
            return ctorValue;

        return null;
    }

    private static string? GetConverterType(IPropertySymbol prop)
    {
        var converterAttr = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString().StartsWith("Pragmatic.Mapping.Attributes.MapConverterAttribute") ==
            true);

        if (converterAttr?.AttributeClass is INamedTypeSymbol { TypeArguments.Length: > 0 } attrType)
            return attrType.TypeArguments[0].ToDisplayString();

        return null;
    }

    /// <summary>The element type of a collection property, or null when the property is not one.</summary>
    private static INamedTypeSymbol? ElementSymbolOf(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);

        if (unwrapped is IArrayTypeSymbol array)
            return array.ElementType as INamedTypeSymbol;

        return unwrapped is INamedTypeSymbol { TypeArguments.Length: 1 } named
            ? named.TypeArguments[0] as INamedTypeSymbol
            : null;
    }

    private static bool HasMapToAttribute(INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Mapping.Attributes.MapToAttribute") == true);
    }

    private static bool HasPatchAttribute(INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Persistence.Patch.PatchAttribute") == true);
    }

    private static (bool isNested, string? nestedType) AnalyzeNestedMutation(ITypeSymbol type)
    {
        // Unwrap nullable
        var unwrapped = UnwrapNullable(type);

        if (unwrapped is not INamedTypeSymbol namedType)
            return (false, null);

        // Check if type has [Mutation<T>] attribute
        var hasMutation = namedType.GetAttributes().Any(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Persistence.Patch.PatchAttribute") == true);

        if (hasMutation)
            return (true, namedType.ToDisplayString());

        return (false, null);
    }

    private static (bool isCollection, CollectionKind kind, bool isElementMutation, string? elementMutationType, string?
        elementEntityType)
        AnalyzeCollection(ITypeSymbol dtoType, ITypeSymbol? entityType)
    {
        var unwrapped = UnwrapNullable(dtoType);

        if (unwrapped is not INamedTypeSymbol namedType)
            return (false, CollectionKind.None, false, null, null);

        // Check if it's a collection type
        var collectionKind = GetCollectionKind(namedType);
        if (collectionKind == CollectionKind.None)
            return (false, CollectionKind.None, false, null, null);

        // Get element type
        var elementType = namedType.TypeArguments.FirstOrDefault();
        if (elementType is null)
            return (true, collectionKind, false, null, null);

        // Check if element has [Mutation<T>]
        var (isElementMutation, elementMutationType) = AnalyzeNestedMutation(elementType);

        // Get entity element type if available
        string? elementEntityType = null;
        if (entityType is INamedTypeSymbol { TypeArguments.Length: > 0 } entityCollectionType)
            elementEntityType = entityCollectionType.TypeArguments[0].ToDisplayString();

        return (true, collectionKind, isElementMutation, elementMutationType, elementEntityType);
    }

    private static CollectionKind GetCollectionKind(INamedTypeSymbol type)
    {
        var typeName = type.OriginalDefinition.ToDisplayString();

        if (typeName.StartsWith("System.Collections.Generic.List"))
            return CollectionKind.List;
        if (typeName.StartsWith("System.Collections.Generic.IList"))
            return CollectionKind.IList;
        if (typeName.StartsWith("System.Collections.Generic.ICollection"))
            return CollectionKind.ICollection;
        if (typeName.StartsWith("System.Collections.Generic.HashSet"))
            return CollectionKind.HashSet;
        if (typeName.StartsWith("System.Collections.Generic.IEnumerable"))
            return CollectionKind.IEnumerable;
        if (typeName.StartsWith("System.Collections.Generic.IReadOnlyList"))
            return CollectionKind.IReadOnlyList;
        if (typeName.StartsWith("System.Collections.Generic.IReadOnlyCollection"))
            return CollectionKind.IReadOnlyCollection;
        if (type.TypeKind == TypeKind.Array)
            return CollectionKind.Array;

        // Check interfaces
        foreach (var iface in type.AllInterfaces)
        {
            var ifaceName = iface.OriginalDefinition.ToDisplayString();
            if (ifaceName.StartsWith("System.Collections.Generic.ICollection"))
                return CollectionKind.ICollection;
            if (ifaceName.StartsWith("System.Collections.Generic.IList"))
                return CollectionKind.IList;
        }

        return CollectionKind.None;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedType)
            return namedType.TypeArguments[0];

        return type;
    }
}
