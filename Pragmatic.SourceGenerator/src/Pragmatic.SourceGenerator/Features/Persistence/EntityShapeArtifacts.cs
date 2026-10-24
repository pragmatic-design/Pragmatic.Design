using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The files that declare an entity's shape: the members and filter classes every engine reads, whichever engine
///     persists it.
/// </summary>
/// <remarks>
///     One list, read twice: <see cref="EntityCoreFeature" /> and <see cref="RepositoryFeature" /> emit these files, and
///     the Sql engine's translation adds the same texts to the compilation it binds its query chains in (no generator sees
///     another's output, so the chains over a generated member would not bind otherwise). Two copies of the shape, one
///     emitted and one bound against, is how they came to differ.
/// </remarks>
internal static class EntityShapeArtifacts
{
    /// <summary>The entity's members: relations, factory, specifications, traits, owner, scopes.</summary>
    public static IEnumerable<Artifact> Members(EntityMetadataModel entity, bool isEfCore)
    {
        if (entity.HasGeneratedRelationProperties)
            yield return new EntityRelationsTemplate(entity).RenderOutput();
        yield return new EntityCreateTemplate(entity).RenderOutput();
        yield return new SpecificationTemplate(entity).RenderOutput();
        yield return new EntityTraitsTemplate(entity, declaresRowVersion: !isEfCore).RenderOutput();
        if (entity.IsOwnedEntity)
            yield return new OwnershipPropertyTemplate(entity).RenderOutput();
        if (entity.IsScopedEntity)
            yield return new ScopedPropertyTemplate(entity).RenderOutput();
    }

    /// <summary>
    ///     The filter classes callers name (<c>Disable&lt;Reservation.OwnershipFilter&gt;()</c>,
    ///     <c>[WithoutFilter&lt;T&gt;]</c>), and a temporal entity's history scope.
    /// </summary>
    public static IEnumerable<Artifact> Filters(EntityMetadataModel entity)
    {
        if (entity.IsSoftDelete)
            yield return new SoftDeleteFilterTemplate(entity).RenderOutput();
        if (entity.IsTenantEntity)
            yield return new TenantFilterTemplate(entity).RenderOutput();

        // Data access filters — mutually exclusive: combined L1+L2 OR individual
        if (entity is { IsOwnedEntity: true, IsScopedEntity: true })
            yield return new DataAccessFilterTemplate(entity).RenderOutput();
        else if (entity.IsOwnedEntity)
            yield return new OwnershipFilterTemplate(entity).RenderOutput();
        else if (entity.IsScopedEntity)
            yield return new ScopedDataFilterTemplate(entity).RenderOutput();

        if (entity.IsTemporalRelation)
        {
            yield return new TemporalFilterTemplate(entity).RenderOutput();
            // The scope that lets a read see closed stretches, on every temporal entity — not only the
            // ones with a MaxActive, which is what gates the validation template.
            yield return new TemporalHistoryScopeTemplate(entity).RenderOutput();
        }
    }

    /// <summary>
    ///     The setters of a declared entity's private-setter properties and its change tracking; null when it has none.
    /// </summary>
    /// <remarks>
    ///     An entity that derives from another entity gets the lightweight template: the full one declares the
    ///     <c>IChangeTracking</c> infrastructure the base's partial already carries.
    /// </remarks>
    public static Artifact? Setters(EntityMetadataModel entity, ImmutableHashSet<string>? cascadeProperties)
    {
        if (!entity.IsValid || !entity.HasPrivateSetterProperties || entity.IsFromReference)
            return null;

        return entity.BaseEntityFullTypeName is not null
            ? new DerivedEntitySettersTemplate(entity).RenderOutput()
            : new EntitySettersTemplate(entity, cascadeProperties).RenderOutput();
    }

    /// <summary>
    ///     Every shape file of every entity an engine of this compilation persists, as the generator emits them: what the
    ///     Sql engine binds its chains against.
    /// </summary>
    /// <remarks>
    ///     ASSUMPTION: the setters are rendered without the cascade sources. Those add the event a cascade listens to inside
    ///     a setter's body, and change no member a chain can read. The setters of derived types that are not declared
    ///     entities (<c>PersistenceFeature.GenerateDerivedTypeSetters</c>, which needs the compilation) and a state
    ///     machine's transitions are not here either: neither is a member a query reads.
    /// </remarks>
    public static EquatableArray<ShapeSourceModel> ForBinding(ImmutableArray<EntityMetadataModel> entities, DetectedFeatures features)
    {
        var sources = new List<ShapeSourceModel>();
        foreach (var entity in entities)
        {
            if (!entity.IsValid || entity.IsFromReference || !PersistenceEngineMarker.HasEngine(entity, features))
                continue;

            var isEfCore = PersistenceEngineMarker.IsEfCore(entity, features);
            foreach (var artifact in Members(entity, isEfCore))
                Add(artifact);
            foreach (var artifact in Filters(entity))
                Add(artifact);
            if (Setters(entity, null) is { } setters)
                Add(setters);
        }

        return sources.ToEquatableArray();

        void Add(Artifact artifact)
        {
            if (!artifact.IsEmpty)
                sources.Add(new ShapeSourceModel(artifact.HintName, artifact.Text));
        }
    }
}
