using Pragmatic.Actions.Invoker;
using Showcase.Billing.Actions;
using Showcase.Booking.Events;

namespace Showcase.Billing.Infrastructure.EventHandlers;

/// <summary>
/// When a reservation is cancelled, voids the associated invoice in Billing.
/// Demonstrates: Cross-boundary event handling (Booking → Billing) via action pipeline.
/// Pattern mirrors ReservationConfirmedHandler — uses only event data, no cross-boundary entity access.
/// </summary>
[EventHandler]
public sealed class ReservationCancelledHandler(
    IVoidDomainActionInvoker<VoidInvoiceForReservationAction> voidInvoice)
    : IDomainEventHandler<ReservationCancelled>
{
    public async Task HandleAsync(ReservationCancelled @event, CancellationToken ct = default)
    {
        var action = new VoidInvoiceForReservationAction
        {
            ReservationId = @event.ReservationId
        };

        await voidInvoice.InvokeAsync(action, ct).ConfigureAwait(false);
    }
}
