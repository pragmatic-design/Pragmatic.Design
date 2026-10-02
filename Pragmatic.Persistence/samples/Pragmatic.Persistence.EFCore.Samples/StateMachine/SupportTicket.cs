using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.StateMachine;

namespace Pragmatic.Persistence.EFCore.Samples.StateMachine;

/// <summary>
///     A support ticket guarded by a generated state machine.
///     <c>[StateMachine&lt;TicketStatus&gt;]</c> makes the SG emit, on this partial class:
///     <list type="bullet">
///         <item><c>bool CanTransitionTo(TicketStatus)</c> — table lookup, no side effects.</item>
///         <item><c>VoidResult&lt;IError&gt; TransitionTo(TicketStatus)</c> — applies the move or returns a ConflictError.</item>
///         <item><c>ReadOnlySpan&lt;TicketStatus&gt; AllowedTransitions()</c> — the legal next states.</item>
///     </list>
///     The <c>Status</c> property uses a private setter so the generated <c>SetStatus</c>
///     (emitted by the entity setters feature) can mutate it from inside <c>TransitionTo</c>.
/// </summary>
[Entity]
[StateMachine<TicketStatus>]
public partial class SupportTicket : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string Subject { get; set; } = "";

    /// <summary>Current state. Mutated only through the generated <c>TransitionTo</c> / <c>SetStatus</c>.</summary>
    public TicketStatus Status { get; private set; } = TicketStatus.Open;
}
