using Pragmatic.Actions.Commit;
using Pragmatic.Privacy;

namespace Showcase.Booking.Reservations.Actions;

/// <summary>
///     Cancels every open reservation of one property — a closure, a refurbishment, a licence pulled.
/// </summary>
/// <remarks>
///     <para>
///         The shape this exists to demonstrate: an action that invokes a mutation of its <b>own</b>
///         boundary in a loop. It is not a <c>[CompositeAction]</c> — the steps are not known at
///         compile time, there are as many as there are reservations — and it is not a list of setters,
///         because each cancellation is a real mutation with its own validation, permission and event.
///     </para>
///     <para>
///         It costs <b>one</b> transaction: each nested invocation finds that this action already owns
///         the boundary's unit of work, stages its writes and defers its events; the action's invoker
///         saves once and flushes them. No step reads what a previous one wrote, so there is nothing
///         here that would justify <c>[Transactional]</c> — which costs a round trip per step and is
///         for exactly that case.
///     </para>
///     <para>
///         The mutation is reached through the boundary's own interface rather than through its invoker,
///         which is the rule and not a preference here: the two are the same call plus one line, and
///         that line is the one that stops the nested permission from being asked once per reservation.
///     </para>
/// </remarks>
[DomainAction]
// What it reaches through the boundary interface below, for the Article 30 register. The register
// derives an action's reach from the types of its dependencies, and IBookingInternalActions names
// none — so without this the action leaves the register, which reads as "processes nothing"
// (PRAG2911). Reservation only: the loop cancels reservations and touches nothing else.
[ProcessesData<Reservation>]
// Once, not PerStep: closing a property is one fact, so half its reservations cancelled is not a state
// to leave behind. Not [Transactional] either — no step reads what a previous one wrote, and that
// would cost a round trip each to buy visibility nobody uses.
[CommitStrategy(CommitMode.Once)]
[Endpoint(HttpVerb.Post, "api/properties/{PropertyId}/cancel-reservations")]
[RequirePermission(BookingPermissions.Reservation.Update)]
public partial class CancelReservationsForPropertyAction : DomainAction<int>
{
    private IRepository<Reservation> _reservations = null!;

    /// <summary>The boundary's own operations, reached by name.</summary>
    /// <remarks>
    ///     ⚠️ The internal interface, not <c>IMutationInvoker&lt;&gt;</c>. Both end at the same invoker
    ///     and share this action's unit of work either way, so the transaction is unchanged — but the
    ///     facade enters <c>ICallContext.EnterInternalCall()</c>, and the raw invoker does not. Through
    ///     the invoker the cancellation's own permission is asked once per reservation; through the
    ///     facade it is asked never, because this action already answered for it at the route.
    /// </remarks>
    private IBookingInternalActions _booking = null!;

    /// <summary>The property being closed.</summary>
    public required Guid PropertyId { get; init; }

    /// <summary>Why, recorded on every cancellation and carried by the event.</summary>
    public required string Reason { get; init; }

    /// <summary>How many reservations were cancelled.</summary>
    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var open = await _reservations
            .FindAsync(ReservationSpecifications.IsActive() & ReservationSpecifications.AtProperty(PropertyId), ct)
            .ConfigureAwait(false);

        foreach (var reservation in open)
        {
            var cancelled = await _booking.Reservations
                .CancelReservation(reservation.Id, Reason, ct)
                .ConfigureAwait(false);

            // One failure fails the whole closure: the reservations share a transaction, so a partial
            // cancellation is not a state this action can leave behind even if it wanted to.
            if (cancelled.IsFailure)
                return Result<int, IError>.Failure(cancelled.Error);
        }

        return open.Count;
    }
}
