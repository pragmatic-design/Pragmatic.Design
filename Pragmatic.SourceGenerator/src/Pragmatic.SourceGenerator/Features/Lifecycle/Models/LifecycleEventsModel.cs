using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Models;

/// <summary>
///     An entity declaring one or more <c>[Raises&lt;TEvent&gt;(on: ...)]</c>. Drives generation of the
///     <c>IRaisesLifecycleEvents</c> partial that raises each event at its lifecycle transition.
/// </summary>
internal sealed record LifecycleEventsModel : GeneratorModel
{
    /// <summary>The declared lifecycle raises, grouped later by lifecycle in the template.</summary>
    public EquatableArray<LifecycleRaise> Raises { get; init; }

    /// <summary>Whether the entity derives from DomainEventSource (so the generated body can call RaiseEvent).</summary>
    public bool IsDomainEventSource { get; init; }

    /// <summary>Event constructor parameters that matched neither a known special nor an entity member ("Event.Param").</summary>
    public EquatableArray<string> UnmatchedParameters { get; init; }

    /// <summary>Location for diagnostics.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();
}
