using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     Runs one outbox delivery pass on demand, without waiting for the background loop.
/// </summary>
/// <remarks>
///     <para>
///         The loop in <see cref="EventOutboxDeliveryService{TContext}" /> calls exactly this, on a
///         timer. Registered by <c>AddEventOutbox&lt;TContext&gt;</c> so an application can call it
///         too — which is what makes an outbox assertable: enqueue, drain, read the effect, with no
///         polling interval in the middle.
///     </para>
///     <para>
///         ⚠️ <b>Generic on the context, and that is the point.</b> An application with two
///         <c>DbContext</c>s that own outboxes gets two registrations, and naming which one to drain
///         is the caller's job. A single non-generic interface would resolve to whichever was
///         registered last and drain the wrong table without saying so.
///     </para>
///     <para>
///         ⚠️ <b>It is not a substitute for the loop.</b> An application that drains by hand and
///         removes the background service has to drain after every write that raises an event, from
///         every path, forever — and the first one somebody forgets is an event that is written down
///         and never delivered, which is the failure the outbox exists to prevent.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The DbContext that owns the outbox table.</typeparam>
public interface IEventOutboxDrainer<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Claims a batch of pending entries, dispatches them, and marks what succeeded.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>One batch, and only the entries eligible when it started.</b> It says nothing
    ///         about anything enqueued while it ran, and nothing about entries another replica had
    ///         already claimed. A caller that has written N events and needs all of them delivered
    ///         drains until the return value is 0, rather than assuming one call is enough — the
    ///         batch size is configurable and defaults to less than "everything".
    ///     </para>
    ///     <para>
    ///         A handler that throws does not fail this call: its entry keeps its row, releases its
    ///         claim and is retried by a later pass, which is what writing the event down was for.
    ///         So a return of 0 means "nothing was delivered", not "nothing was pending".
    ///     </para>
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many entries were delivered and marked processed by this pass.</returns>
    Task<int> DrainOnceAsync(CancellationToken ct = default);
}
