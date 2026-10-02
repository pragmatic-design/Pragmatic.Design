using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Extracts DTO metadata from [MapFrom&lt;TEntity&gt;] decorated types for include detection.
/// </summary>
internal static class DtoIncludeTransform
{
    private const string GenerateProjectionAttribute = "Pragmatic.Mapping.Attributes.GenerateProjectionAttribute";

    /// <summary>
    ///     Transforms a [MapFrom&lt;TEntity&gt;]-decorated type into DTO metadata.
    /// </summary>
    public static DtoMetadataInfo? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        // Get entity type from MapFrom<TEntity> generic argument
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        var entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return null;

        // Collect DTO property names
        var propertyNames = targetSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p is { IsIndexer: false, GetMethod: not null })
            .Select(p => p.Name)
            .ToImmutableArray();

        // Check for [GenerateProjection]
        var hasProjection = targetSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == GenerateProjectionAttribute);

        var dtoFullTypeName = targetSymbol.ContainingNamespace.IsGlobalNamespace
            ? targetSymbol.Name
            : $"{targetSymbol.ContainingNamespace.ToDisplayString()}.{targetSymbol.Name}";

        return new DtoMetadataInfo
        {
            DtoTypeName = targetSymbol.Name,
            DtoFullTypeName = dtoFullTypeName,
            EntityFullTypeName = entityType.ToDisplayString(),
            PropertyNames = propertyNames,
            HasProjection = hasProjection
        };
    }

    /// <summary>
    ///     Combines DTO metadata with entity metadata to produce include models.
    ///     Groups DTOs by entity and matches DTO properties to entity navigations.
    /// </summary>
    public static ImmutableArray<DtoIncludeModel> BuildIncludeModels(
        ImmutableArray<EntityMetadataModel> entities,
        ImmutableArray<DtoMetadataInfo> dtos)
    {
        if (entities.Length == 0 || dtos.Length == 0)
            return ImmutableArray<DtoIncludeModel>.Empty;

        // Build entity lookup by full type name
        var entityLookup = entities
            .Where(e => e.IsValid)
            .ToDictionary(e => e.FullTypeName, e => e);

        // Group DTOs by entity
        var dtosByEntity = dtos
            .Where(d => entityLookup.ContainsKey(d.EntityFullTypeName))
            .GroupBy(d => d.EntityFullTypeName);

        var builder = ImmutableArray.CreateBuilder<DtoIncludeModel>();

        foreach (var group in dtosByEntity)
        {
            var entity = entityLookup[group.Key];
            var navigations = entity.Navigations;

            // Skip entities with no navigations
            if (navigations.Length == 0)
                continue;

            // The navigations that exist as members: same boundary, or a crossing the boundary reads
            // through [ReadAccess<T>]. A crossing without it has only a key, so there is nothing to
            // include.
            var sameBoundaryNavNames = navigations
                .Where(n => !n.IsUnreadableCrossing(entity))
                .Select(n => n.Name)
                .ToImmutableArray();

            if (sameBoundaryNavNames.Length == 0)
                continue;

            // One entry per DTO, with no navigation matching at all. What a DTO reaches through is
            // decided by Mapping and published as RequiredNavigations; matching property names against
            // navigation names answered "none" for every DTO that flattens a path, which is most of
            // them. An empty list makes the generated method a no-op, which is the right no-op.
            var dtoEntries = ImmutableArray.CreateBuilder<DtoIncludeEntry>();

            foreach (var dto in group)
            {
                dtoEntries.Add(new DtoIncludeEntry
                {
                    DtoTypeName = dto.DtoTypeName,
                    DtoFullTypeName = dto.DtoFullTypeName,
                });
            }

            // Only generate if at least one DTO needs includes or we have navigations for WithAllRelations
            var model = new DtoIncludeModel
            {
                Namespace = entity.Namespace,
                EntityTypeName = entity.TypeName,
                EntityFullTypeName = entity.FullTypeName,
                AllNavigationNames = sameBoundaryNavNames,
                Dtos = dtoEntries.ToImmutable()
            };

            builder.Add(model);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Builds query extension models for DTOs with [GenerateProjection].
    ///     Each model groups DTOs by entity and carries enough info to generate
    ///     GetAs{Dto}Async, ListAs{Dto}Async, and As{Dto} extension methods.
    /// </summary>
    public static ImmutableArray<DtoQueryModel> BuildQueryModels(
        ImmutableArray<EntityMetadataModel> entities,
        ImmutableArray<DtoMetadataInfo> dtos,
        ImmutableArray<DtoIncludeModel> includeModels)
    {
        if (entities.Length == 0 || dtos.Length == 0)
            return ImmutableArray<DtoQueryModel>.Empty;

        // Only DTOs with [GenerateProjection] get query extensions
        var projectionDtos = dtos.Where(d => d.HasProjection).ToList();
        if (projectionDtos.Count == 0)
            return ImmutableArray<DtoQueryModel>.Empty;

        // Build entity lookup
        var entityLookup = entities
            .Where(e => e.IsValid)
            .ToDictionary(e => e.FullTypeName, e => e);

        // Build include model lookup (to know if WithIncludesFor{Dto} exists)
        var includeEntryLookup = new HashSet<string>();
        foreach (var incModel in includeModels)
        {
            foreach (var entry in incModel.Dtos)
                includeEntryLookup.Add($"{incModel.EntityFullTypeName}:{entry.DtoTypeName}");
        }

        // Group projection DTOs by entity
        var dtosByEntity = projectionDtos
            .Where(d => entityLookup.ContainsKey(d.EntityFullTypeName))
            .GroupBy(d => d.EntityFullTypeName);

        var builder = ImmutableArray.CreateBuilder<DtoQueryModel>();

        foreach (var group in dtosByEntity)
        {
            var entity = entityLookup[group.Key];

            var dtoEntries = ImmutableArray.CreateBuilder<DtoQueryEntry>();

            foreach (var dto in group)
            {
                dtoEntries.Add(new DtoQueryEntry
                {
                    DtoTypeName = dto.DtoTypeName,
                    DtoFullTypeName = dto.DtoFullTypeName,
                });
            }

            builder.Add(new DtoQueryModel
            {
                Namespace = entity.Namespace,
                EntityTypeName = entity.TypeName,
                EntityFullTypeName = entity.FullTypeName,
                IdType = entity.IdType,
                Dtos = dtoEntries.ToImmutable()
            });
        }

        return builder.ToImmutable();
    }
}
