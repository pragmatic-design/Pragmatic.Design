using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>A cache-safe description of one navigation between entities on different engines (<c>PRAG0745</c>).</summary>
/// <param name="Entity">The entity that declares the navigation.</param>
/// <param name="Navigation">The navigation's name.</param>
/// <param name="EntityEngine">The engine that persists <paramref name="Entity" />.</param>
/// <param name="Target">The entity the navigation reaches.</param>
/// <param name="TargetEngine">The engine that persists <paramref name="Target" />.</param>
/// <param name="LocationInfo">The declaring entity, where the relation is written; null for one from a referenced assembly.</param>
internal sealed record RelationBetweenEngines(
    string Entity,
    string Navigation,
    string EntityEngine,
    string Target,
    string TargetEngine,
    LocationInfo? LocationInfo);
