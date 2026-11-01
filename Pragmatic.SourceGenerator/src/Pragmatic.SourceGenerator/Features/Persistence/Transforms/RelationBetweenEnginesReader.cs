using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Finds the navigations that would join entities on different engines (<c>PRAG0745</c>).
/// </summary>
/// <remarks>
///     <para>
///         A relation across boundaries already generates only its key, unless the boundary reads the other side with
///         <c>[ReadAccess&lt;T&gt;]</c>: then the navigation is generated, because both tables sit in one
///         <c>DbContext</c>. Across engines they do not, and no engine joins a table it does not hold. A many-to-many
///         generates its collection even without <c>[ReadAccess]</c>, so it is refused whatever the boundary reads.
///     </para>
///     <para>
///         Every entity, those of the referenced modules included: the host is where two engines first meet, as for
///         <see cref="PersistenceEngineChoiceReader" />. An entity whose engine is undecided (<c>PRAG0743</c>) is left to
///         that diagnostic.
///     </para>
/// </remarks>
internal static class RelationBetweenEnginesReader
{
    public static EquatableArray<RelationBetweenEngines> Read(ImmutableArray<EntityMetadataModel> entities, DetectedFeatures features)
    {
        if (PersistenceEngineMarker.Referenced(features).Length < 2)
            return EquatableArray<RelationBetweenEngines>.Empty;

        var byFullName = new Dictionary<string, EntityMetadataModel>(StringComparer.Ordinal);
        foreach (var entity in entities)
            byFullName[StripGlobal(entity.FullTypeName)] = entity;

        var found = ImmutableArray.CreateBuilder<RelationBetweenEngines>();
        foreach (var entity in entities)
        {
            if (PersistenceEngineMarker.EngineOf(entity, features) is not { } engine)
                continue;

            foreach (var navigation in entity.Navigations)
            {
                // An owned type is a column group of this table, and a lookup is read from its cache, not joined.
                if (navigation.IsOwned || navigation.IsLookupReference)
                    continue;

                // Only the key crosses: no member, no join.
                if (navigation.NavigationType != "ManyToMany" && navigation.IsUnreadableCrossing(entity))
                    continue;

                if (!byFullName.TryGetValue(StripGlobal(navigation.TargetFullTypeName ?? navigation.TargetTypeName), out var target)
                    || PersistenceEngineMarker.EngineOf(target, features) is not { } targetEngine
                    || targetEngine == engine)
                    continue;

                found.Add(new RelationBetweenEngines(
                    StripGlobal(entity.FullTypeName),
                    navigation.Name,
                    engine,
                    StripGlobal(target.FullTypeName),
                    targetEngine,
                    entity.DeclarationLocation));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>Reports each relation <see cref="Read" /> found, on the entity that declares it.</summary>
    public static void Report(SourceProductionContext context, EquatableArray<RelationBetweenEngines> relations)
    {
        foreach (var relation in relations)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceEngineDiagnostics.RelationBetweenEngines,
                relation.LocationInfo?.ToLocation(),
                relation.Entity,
                relation.Navigation,
                relation.EntityEngine,
                relation.Target,
                relation.TargetEngine));
        }
    }

    private static string StripGlobal(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;
}
