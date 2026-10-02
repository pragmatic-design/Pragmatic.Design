using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Models;

/// <summary>
///     One declared lifecycle raise: at <see cref="Lifecycle"/> the entity raises <see cref="EventType"/>,
///     constructed from <see cref="Args"/> (entity member expressions matched to the event ctor by name).
/// </summary>
internal sealed record LifecycleRaise(string Lifecycle, string EventType, EquatableArray<string> Args);
