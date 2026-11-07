using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     How the orchestrator builds a step's <c>[CompensateWith&lt;T&gt;]</c> from the saga's state, decided at compile time (#532): the
///     constructor's arguments and the members it sets, each an expression over the saga (<c>instance</c>). A member is matched to a saga
///     property by name, ignoring case, as the JSON round trip it replaces matched them; a required member the saga has no value for is set to
///     its default, as that round trip left it.
/// </summary>
internal sealed record CompensationSeedModel
{
    /// <summary>False when the compensator cannot be built: abstract, an interface, or no constructor to choose. The compensation is skipped.</summary>
    public bool CanConstruct { get; init; }

    /// <summary>The constructor's arguments, named: <c>stayId: instance.StayId</c>.</summary>
    public EquatableArray<string> Arguments { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The members the initializer sets: <c>Nights = instance.Nights</c>, <c>Reason = default!</c>.</summary>
    public EquatableArray<string> Assignments { get; init; } = EquatableArray<string>.Empty;
}
