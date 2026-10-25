using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using SqlDiagnostics = Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     The engine of each loaded entity, decided against the engines this compilation references: a load with includes reads through EF Core's
///     <c>Include</c> or the Sql engine's compiled read, and the per-symbol transform, which has no references to ask, only reads what the
///     entity's boundary names.
/// </summary>
internal static partial class ActionsFeature
{
    private static EquatableArray<LoadEntityModel> ResolveLoadEngines(EquatableArray<LoadEntityModel> loads, DetectedFeatures features)
        => loads.Count == 0
            ? loads
            : loads.Select(load => load with { Engine = PersistenceEngineMarker.EngineOf(load.EntityNamedEngine, features) }).ToEquatableArray();

    /// <summary>
    ///     The reads with navigations the mutations' invokers ask of the Sql engine (<see cref="MutationModel.EngineLoadPaths" />), for its
    ///     generator to compile one per set in the entity's repository.
    /// </summary>
    private static IncrementalValueProvider<EquatableArray<EngineReadModel>> EngineReads(IncrementalValueProvider<ImmutableArray<MutationModel>> mutations)
        => mutations.Select(static (models, _) => models
            .Where(m => m.Engine == PersistenceEngineMarker.Sql && m.IdPropertyName is not null && m.EngineLoadPaths.Count > 0)
            .Select(m => new EngineReadModel(m.EntityFullTypeName, m.EngineLoadPaths))
            .Distinct()
            .ToEquatableArray());

    /// <summary>
    ///     What a mutation asks that its engine does not do: on the Sql engine, children written through the entity (merged into it, or
    ///     hidden and brought back by a soft delete that cascades), which its unit of work would not save (PRAG3037); on the documents engine,
    ///     any navigation loaded or child written (PRAG3112).
    /// </summary>
    private static void ReportEngineRefusals(SourceProductionContext ctx, MutationModel mutation)
    {
        var children = mutation.Children.Where(c => c.IsWritable).Select(c => c.TargetPropertyName).ToList();
        // A cascading delete or restore writes the children it hides or brings back: the same graph, reached by another path. The engine
        // load does not read them either, so without this it would mark the entity alone, silently.
        if ((mutation.IsDelete || mutation.IsRestore) && mutation.SoftDelete is { HasCascadeTargets: true } softDelete)
        {
            foreach (var target in softDelete.CascadeTargets)
            {
                if (!children.Contains(target.PropertyName))
                    children.Add(target.PropertyName);
            }
        }

        if (mutation.Engine == PersistenceEngineMarker.Sql && children.Count > 0)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(SqlDiagnostics.MutationWritesChildren, mutation.LocationInfo?.ToLocation(),
                mutation.TypeName, mutation.EntityTypeName, string.Join(", ", children)));
        }
        else if (mutation.Engine == PersistenceEngineMarker.Documents && (children.Count > 0 || mutation.EngineLoadPaths.Count > 0))
        {
            var what = children.Count > 0
                ? $"writes the children {string.Join(", ", children)}"
                : $"loads the navigations {string.Join(", ", mutation.EngineLoadPaths)}";
            ctx.ReportDiagnostic(Diagnostic.Create(DocDiagnostics.MutationNavigationsNotSupported, mutation.LocationInfo?.ToLocation(),
                mutation.TypeName, what, mutation.EntityTypeName));
        }
    }
}
