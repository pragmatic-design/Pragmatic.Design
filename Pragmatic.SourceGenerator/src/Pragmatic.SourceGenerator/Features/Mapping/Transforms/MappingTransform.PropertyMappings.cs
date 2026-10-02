using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Transforms;

/// <summary>
///     Property extraction and resolution methods for MappingTransform.
/// </summary>
internal static partial class MappingTransform
{
    private static ImmutableArray<PropertyMappingModel> ExtractPropertyMappings(
        INamedTypeSymbol targetSymbol,
        INamedTypeSymbol sourceSymbol,
        Compilation compilation,
        CancellationToken ct,
        int projectionMaxDepth = 5)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();
        var sourceProperties = PropertyAnalyzer.GetAllProperties(sourceSymbol);

        foreach (var targetProp in PropertyAnalyzer.GetAllProperties(targetSymbol))
        {
            ct.ThrowIfCancellationRequested();

            var mapping = AnalyzePropertyMapping(
                targetProp, sourceSymbol, sourceProperties, compilation, projectionMaxDepth);
            builder.Add(mapping);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<PropertyMappingModel> ExtractPropertyMappingsForMapTo(
        INamedTypeSymbol dtoSymbol,
        INamedTypeSymbol entitySymbol,
        Compilation compilation,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();
        var entityProperties = PropertyAnalyzer.GetAllProperties(entitySymbol);

        foreach (var dtoProp in PropertyAnalyzer.GetAllProperties(dtoSymbol))
        {
            ct.ThrowIfCancellationRequested();

            var mapping = AnalyzePropertyMappingForMapTo(dtoProp, entitySymbol, entityProperties, compilation);
            builder.Add(mapping);
        }

        return builder.ToImmutable();
    }

    private static (MappingResolution resolution, string? expr, string? type, bool nullable, bool sourceIsEnum, bool ambiguous)
        ResolvePropertyMapping(
            string propertyName,
            INamedTypeSymbol sourceSymbol,
            ImmutableArray<IPropertySymbol> sourceProperties)
    {
        // Priority 2: Direct name match
        var directMatch = sourceProperties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (directMatch is not null)
        {
            // PRAG0323: the flattening convention would ALSO match (e.g. DTO "CustomerName" with a
            // source having both CustomerName and Customer.Name). Direct match wins; warn to be explicit.
            // The EF FK convention ({Nav}Id property + {Nav}.Id) is excluded: both resolve to the same
            // value by design, so it is not a real ambiguity (it would flag every foreign key).
            var alsoFlattens = !propertyName.EndsWith("Id", StringComparison.Ordinal)
                && PropertyPathResolver.TryFlatten(propertyName, sourceSymbol).success;
            return (
                MappingResolution.DirectMatch,
                $"entity.{directMatch.Name}",
                directMatch.Type.ToDisplayString(),
                directMatch.Type.NullableAnnotation == NullableAnnotation.Annotated,
                TypeConversionHelper.IsEnumType(directMatch.Type),
                alsoFlattens);
        }

        // Priority 2.5: Naming-convention match (snake_case source → PascalCase DTO): compare with
        // underscores stripped, case-insensitive (e.g. customer_name → CustomerName).
        var normalizedTarget = propertyName.Replace("_", "");
        var conventionMatch = sourceProperties.FirstOrDefault(p =>
            string.Equals(p.Name.Replace("_", ""), normalizedTarget, StringComparison.OrdinalIgnoreCase));
        if (conventionMatch is not null)
            return (
                MappingResolution.DirectMatch,
                $"entity.{conventionMatch.Name}",
                conventionMatch.Type.ToDisplayString(),
                conventionMatch.Type.NullableAnnotation == NullableAnnotation.Annotated,
                TypeConversionHelper.IsEnumType(conventionMatch.Type),
                false);

        // Priority 3: Flattening convention (e.g., AddressCity -> Address.City)
        var flattenResult = PropertyPathResolver.TryFlatten(propertyName, sourceSymbol);
        if (flattenResult.success)
            return (MappingResolution.Flattening, flattenResult.expr, flattenResult.type, flattenResult.nullable,
                flattenResult.isEnum, false);

        // Priority 4: Concatenation convention (e.g., FullName -> FirstName + LastName)
        var concatResult = PropertyPathResolver.TryConcatenation(propertyName, sourceProperties);
        if (concatResult.success)
            return (MappingResolution.Concatenation, concatResult.expr, "string", false, false, false);

        // Priority 5: Trait-generated properties (EntityTraitsTemplate will generate these)
        var traitMatch = TryResolveTraitProperty(propertyName, sourceSymbol);
        if (traitMatch.resolution != MappingResolution.None)
            return (traitMatch.resolution, traitMatch.expr, traitMatch.type, traitMatch.nullable,
                traitMatch.sourceIsEnum, false);

        return (MappingResolution.None, null, null, false, false, false);
    }

    /// <summary>
    ///     Checks if the property name matches a trait-generated property on the source entity.
    ///     This handles properties like Id, CreatedAt, IsDeleted, etc. that are generated by
    ///     EntityTraitsTemplate and not yet visible on the INamedTypeSymbol.
    /// </summary>
    private static (MappingResolution resolution, string? expr, string? type, bool nullable, bool sourceIsEnum)
        TryResolveTraitProperty(string propertyName, INamedTypeSymbol sourceSymbol)
    {
        var traitProperties = TraitPropertyResolver.GetGeneratedProperties(sourceSymbol);
        if (traitProperties.IsEmpty)
            return (MappingResolution.None, null, null, false, false);

        foreach (var vp in traitProperties)
        {
            if (string.Equals(vp.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                var isNullable = vp.TypeFullName.EndsWith("?");
                var cleanType = isNullable ? vp.TypeFullName.TrimEnd('?') : vp.TypeFullName;
                return (
                    MappingResolution.DirectMatch,
                    $"entity.{ColumnBehind(vp.Name)}",
                    cleanType,
                    isNullable,
                    false);
            }
        }

        return (MappingResolution.None, null, null, false, false);
    }

    /// <summary>
    ///     The member that is actually a column — <c>PersistenceId</c> where the predicted name is the
    ///     generated <c>Id</c> alias.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The traits generator writes <c>public Guid Id => PersistenceId;</c>: a computed property
    ///         over the key, mapped to no column of its own. A projection naming it cannot be
    ///         translated, so EF materialises the entity and evaluates the projection in memory — the
    ///         reason projections exist, switched off. Measured on a consumer, dropping <c>Id</c> from a
    ///         DTO took the same <c>SELECT</c> from nine columns to three.
    ///     </para>
    ///     <para>
    ///         ⚠️ It survived because every observable signal looked right: one statement, correct
    ///         data. The suites watch the statement count, and the statement count does not move.
    ///     </para>
    ///     <para>
    ///         Only the <b>predicted</b> alias is redirected, and only this branch sees predictions: an
    ///         <c>Id</c> the author declared resolves as a direct match one method above and is left as
    ///         written, because it is a real member and pointing it at a <c>PersistenceId</c> that may
    ///         not exist would emit code that does not compile.
    ///     </para>
    /// </remarks>
    private static string ColumnBehind(string predictedName)
        => predictedName == "Id" ? "PersistenceId" : predictedName;
}
