using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     Nested projection extraction methods for MappingTransform.
/// </summary>
internal static partial class MappingTransform
{
    /// <summary>
    ///     Extracts property mappings for nested DTO projection inlining.
    ///     Recursively extracts mappings for deep nesting (3+ levels).
    ///     <paramref name="depthCapped"/> reports truncation at <paramref name="maxDepth"/>
    ///     (user-configurable via [GenerateProjection(MaxDepth = ...)]) so PRAG0327 can surface it.
    /// </summary>
    private static ImmutableArray<NestedPropertyMapping> ExtractNestedProjectionMappings(
        ITypeSymbol nestedDtoType,
        Compilation compilation,
        out bool droppedComplexMapping,
        out bool depthCapped,
        int depth = 0,
        int maxDepth = 5)
    {
        droppedComplexMapping = false;
        depthCapped = false;

        // Prevent runaway recursion; deeper members are omitted and reported (PRAG0327).
        if (depth >= maxDepth)
        {
            depthCapped = true;
            return ImmutableArray<NestedPropertyMapping>.Empty;
        }

        // Unwrap nullable
        var unwrapped = TypeAnalyzer.UnwrapNullable(nestedDtoType);
        if (unwrapped is not INamedTypeSymbol dtoSymbol)
            return ImmutableArray<NestedPropertyMapping>.Empty;

        // Find [MapFrom<TEntity>] attribute
        var mapFromAttr = dtoSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);

        if (mapFromAttr?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return ImmutableArray<NestedPropertyMapping>.Empty;

        var sourceEntityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (sourceEntityType is null)
            return ImmutableArray<NestedPropertyMapping>.Empty;

        // Get source entity properties
        var sourceProperties = PropertyAnalyzer.GetAllProperties(sourceEntityType);

        // …plus the ones the generators will add. The navigations a [Relation.*] produces do not
        // exist yet in this compilation, and without them a DTO that flattens through one
        // ("WorkItem.Description") or nests a list of them resolves to nothing — dropped in silence.
        var generatedProperties = TraitPropertyResolver.GetGeneratedProperties(sourceEntityType);

        // Get DTO properties and find direct matches
        var builder = ImmutableArray.CreateBuilder<NestedPropertyMapping>();
        foreach (var dtoProp in PropertyAnalyzer.GetAllProperties(dtoSymbol))
        {
            // Skip properties with [MapIgnore]
            // The projection is the read path: only an ignore that covers reading applies.
            if (AttributeAnalyzer.IsIgnoredFor(dtoProp, writing: false))
                continue;

            // Converters and format strings cannot be inlined into a projection (not SQL-translatable).
            var hasConverter = AttributeAnalyzer.GetConverterAttribute(dtoProp) is not null;
            var mapProp = AttributeAnalyzer.GetMapPropertyAttribute(dtoProp);
            if (hasConverter || !string.IsNullOrEmpty(mapProp?.Format))
            {
                // Record the drop so the feature can surface PRAG0326 instead of silently
                // degrading.
                droppedComplexMapping = true;
                continue;
            }

            // Flattening (dotted path) and concatenation (multi-path) ARE expression-tree-safe:
            // inline them. First segments validated against the source entity.
            if (mapProp is not null
                && (mapProp.SourcePaths.Length > 1 || mapProp.SourcePaths.Any(p => p.Contains('.'))))
            {
                var allResolve = mapProp.SourcePaths.All(p =>
                {
                    var head = p.Split('.')[0];
                    return sourceProperties.Any(sp => string.Equals(sp.Name, head, StringComparison.OrdinalIgnoreCase))
                           || generatedProperties.Any(gp => string.Equals(gp.Name, head, StringComparison.OrdinalIgnoreCase));
                });
                if (!allResolve)
                {
                    droppedComplexMapping = true;
                    continue;
                }

                builder.Add(new NestedPropertyMapping
                {
                    TargetPropertyName = dtoProp.Name,
                    SourcePropertyName = mapProp.SourcePaths[0].Split('.')[0],
                    PropertyType = dtoProp.Type.ToDisplayString(),
                    IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                    SourcePaths = mapProp.SourcePaths,
                    Separator = mapProp.Separator,
                    EnumJoinParts = EnumPartsOf(mapProp.SourcePaths, sourceProperties)
                });
                continue;
            }

            // Find direct match in source entity
            var sourcePropName = mapProp?.SourcePaths.Length == 1
                ? mapProp.SourcePaths[0]
                : dtoProp.Name;

            var sourceMatch = sourceProperties.FirstOrDefault(p =>
                string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase));

            string sourceName;
            ITypeSymbol? sourceType;

            if (sourceMatch is not null)
            {
                sourceName = sourceMatch.Name;
                sourceType = sourceMatch.Type;
            }
            else
            {
                // Not declared — but a generator may still be about to add it. Falling straight to
                // `continue` here is what dropped foreign keys and relation navigations from the
                // projection without a word.
                TraitPropertyResolver.VirtualProperty? generated = null;
                foreach (var candidate in generatedProperties)
                {
                    if (!string.Equals(candidate.Name, sourcePropName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    generated = candidate;
                    break;
                }

                if (generated is null)
                    continue;

                sourceName = generated.Value.Name;
                sourceType = generated.Value.TypeSymbol;
            }

            // A generated trait or foreign key carries no symbol: it is scalar by construction, so
            // it maps directly.
            // A [ValueObject] — and a Money, which the persistence generator maps the same way without
            // the attribute — belongs with the simple types here: EF maps it as a complex type and
            // projects it whole. Without this it matched none of the three branches below and was
            // dropped from the inlined projection without even setting the dropped flag — so the
            // nested read answered with the DTO's initialiser and no diagnostic said why.
            if (sourceType is null
                || TypeAnalyzer.IsSimpleType(sourceType)
                || SqlTranslatableAnalyzer.IsProjectedWhole(sourceType))
            {
                builder.Add(new NestedPropertyMapping
                {
                    TargetPropertyName = dtoProp.Name,
                    SourcePropertyName = sourceName,
                    PropertyType = dtoProp.Type.ToDisplayString(),
                    IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                    // What the source can hold, so a non-nullable target gets the default the top level
                    // gives it instead of an assignment that does not compile.
                    SourceIsNullable = sourceType?.NullableAnnotation == NullableAnnotation.Annotated,
                    SourcePropertyType = sourceType?.ToDisplayString(),
                    SourceIsEnum = TypeAnalyzer.UnwrapNullable(sourceType) is { TypeKind: TypeKind.Enum },
                    // The nested source is known only when the initializer is written, so the body is
                    // kept over a placeholder the template fills in.
                    ProjectableBody = sourceMatch is null
                        ? null
                        : ProjectableBody.Of(sourceMatch, compilation, ProjectableBody.PortableSource)
                });
                continue;
            }

            // A LocalizedString read as a string: its value, as the top-level projection reads it. It is
            // neither simple nor a DTO, and without this branch it fell through the loop — the member
            // missing from the initializer and nothing said.
            if (LocalizedStringType.Matches(sourceType.ToDisplayString())
                && dtoProp.Type.SpecialType == SpecialType.System_String)
            {
                builder.Add(new NestedPropertyMapping
                {
                    TargetPropertyName = dtoProp.Name,
                    SourcePropertyName = sourceName,
                    PropertyType = dtoProp.Type.ToDisplayString(),
                    IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                    ReadsLocalizedValue = true,
                    SourceIsNullable = sourceType.NullableAnnotation == NullableAnnotation.Annotated
                });
                continue;
            }

            // Check for nested DTO (non-collection)
            var nestedInfo = NestedDtoAnalyzer.AnalyzeNestedDto(dtoProp.Type, new CollectionInfo());
            if (nestedInfo.IsNested)
            {
                var nestedMappings = ExtractNestedProjectionMappings(dtoProp.Type, compilation, out var nestedDropped, out var nestedCapped, depth + 1, maxDepth);
                droppedComplexMapping |= nestedDropped;
                depthCapped |= nestedCapped;
                builder.Add(new NestedPropertyMapping
                {
                    TargetPropertyName = dtoProp.Name,
                    SourcePropertyName = sourceName,
                    PropertyType = dtoProp.Type.ToDisplayString(),
                    IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                    IsDeclaredNonNull = dtoProp.Type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.NotAnnotated },
                    IsNestedDto = true,
                    NestedDtoType = nestedInfo.DtoType,
                    NestedMappings = nestedMappings
                });
                continue;
            }

            // Check for collection of DTOs
            var collectionInfo = CollectionAnalyzer.AnalyzeCollectionType(dtoProp.Type);
            if (collectionInfo.Kind != CollectionKind.None && collectionInfo.ElementTypeSymbol is not null)
            {
                var elementNestedInfo =
                    NestedDtoAnalyzer.AnalyzeNestedDto(collectionInfo.ElementTypeSymbol, new CollectionInfo());
                if (elementNestedInfo.IsNested || HasMapFromAttribute(collectionInfo.ElementTypeSymbol))
                {
                    var elementMappings = ExtractNestedProjectionMappings(collectionInfo.ElementTypeSymbol, compilation, out var elementDropped, out var elementCapped, depth + 1, maxDepth);
                    droppedComplexMapping |= elementDropped;
                    depthCapped |= elementCapped;
                    var elementDtoType = elementNestedInfo.DtoType ??
                                         collectionInfo.ElementTypeSymbol.ToDisplayString(SymbolDisplayFormat
                                             .FullyQualifiedFormat);
                    builder.Add(new NestedPropertyMapping
                    {
                        TargetPropertyName = dtoProp.Name,
                        SourcePropertyName = sourceName,
                        PropertyType = dtoProp.Type.ToDisplayString(),
                        IsNullable = dtoProp.Type.NullableAnnotation == NullableAnnotation.Annotated,
                        IsCollection = true,
                        CollectionKind = collectionInfo.Kind,
                        ElementDtoType = elementDtoType,
                        ElementMappings = elementMappings
                    });
                    continue;
                }
            }

            // A source the initializer cannot place: the member keeps the DTO's default. Reported
            // (PRAG0326) rather than left out in silence, which is how a localized name went missing.
            droppedComplexMapping = true;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Checks if a type has [MapFrom] attribute.
    /// </summary>
    private static bool HasMapFromAttribute(ITypeSymbol type)
    {
        var unwrapped = TypeAnalyzer.UnwrapNullable(type);
        if (unwrapped is not INamedTypeSymbol namedType)
            return false;

        return namedType.GetAttributes()
            .Any(a => a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Mapping.Attributes.MapFromAttribute") == true);
    }

    /// <summary>
    ///     The paths of a join whose type is an <c>enum</c>, with their members.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Only single-segment paths. A dotted path reaches through a navigation, and resolving the
    ///     type at the end of one is a different job from reading a property of this entity; such a part
    ///     keeps the concatenation it had, which is the behaviour every join had before this existed.
    /// </remarks>
    private static ImmutableArray<EnumJoinPart> EnumPartsOf(
        EquatableArray<string> sourcePaths,
        ImmutableArray<IPropertySymbol> sourceProperties)
    {
        var builder = ImmutableArray.CreateBuilder<EnumJoinPart>();

        foreach (var path in sourcePaths.AsImmutableArray())
        {
            if (path.Contains('.'))
                continue;

            var property = sourceProperties.FirstOrDefault(
                p => string.Equals(p.Name, path, StringComparison.OrdinalIgnoreCase));

            if (property?.Type is not INamedTypeSymbol named)
                continue;

            // A nullable enum is an enum: the chain's final branch answers for the null.
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                named = named.TypeArguments[0] as INamedTypeSymbol ?? named;

            if (named.TypeKind != TypeKind.Enum)
                continue;

            builder.Add(new EnumJoinPart
            {
                Path = path,
                TypeName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Members = named.GetMembers().OfType<IFieldSymbol>()
                    .Where(f => f.IsConst)
                    .Select(f => f.Name)
                    .ToImmutableArray()
            });
        }

        return builder.ToImmutable();
    }
}
