using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One <c>[FromClock]</c> property of a query: what the invoker writes into it, and whether it can.
/// </summary>
internal sealed record ClockBindingModel
{
    /// <summary>The query property the invoker writes.</summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The member of <c>IClock</c> that gives the value — <c>UtcToday</c> for a <c>DateOnly</c>,
    ///     <c>UtcNow</c> for a <c>DateTimeOffset</c> — or null when the type is neither.
    /// </summary>
    public string? ClockMember { get; init; }

    /// <summary>Why the binding cannot be written — the tail of PRAG0734 — or null when it can.</summary>
    public string? Problem { get; init; }

    /// <summary>Where the property is declared, for PRAG0734.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>Whether the invoker writes the binding.</summary>
    public bool IsRendered => Problem is null;
}
