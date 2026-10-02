using Pragmatic.Persistence.StateMachine;

namespace Pragmatic.Persistence.EFCore.Samples.StateMachine;

/// <summary>
///     Lifecycle states for a support ticket.
///     Demonstrates <c>[InitialState]</c> and <c>[TransitionFrom]</c> — the SG reads these
///     to build a compile-time transition table for <see cref="SupportTicket"/>.
/// </summary>
public enum TicketStatus
{
    /// <summary>Freshly opened — the only legal starting state.</summary>
    [InitialState]
    Open,

    [TransitionFrom(Open)]
    InProgress,

    [TransitionFrom(InProgress)]
    Resolved,

    [TransitionFrom(Resolved)]
    Closed,

    // Can be cancelled from any of the early states.
    [TransitionFrom(Open)]
    [TransitionFrom(InProgress)]
    Cancelled
}
