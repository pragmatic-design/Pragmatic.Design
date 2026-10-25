using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

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
}
