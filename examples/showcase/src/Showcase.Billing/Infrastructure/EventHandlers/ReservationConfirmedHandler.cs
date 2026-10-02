using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Showcase.Booking.Events;

namespace Showcase.Billing.Infrastructure.EventHandlers;

/// <summary>
/// When a reservation is confirmed, automatically creates a draft invoice
/// via the Billing boundary interface with unwrapped parameters.
/// Uses only event data — no cross-boundary entity access.
/// Demonstrates: [MessageHandler] with [Retry] for transient failure resilience.
/// </summary>
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 200)]
internal sealed partial class ReservationConfirmedHandler(
    // The internal interface, because CreateDraftInvoice has no endpoint and is not Billing's surface:
    // it is how this module reacts to an event of its own. Same assembly, so the internal one is in
    // reach — which is the whole reason it is generated.
    IBillingInternalActions billingActions) : IMessageHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, MessageContext context, CancellationToken ct = default)
    {
        var taxAmount = Invoice.CalculateTax(@event.TotalAmount);

        await billingActions.CreateDraftInvoice(
            reservationId: @event.ReservationId,
            guestId: @event.GuestId,
            subTotal: @event.TotalAmount,
            taxAmount: taxAmount,
            totalAmount: @event.TotalAmount + taxAmount,
            currency: @event.Currency,
            issuedAt: @event.OccurredAt,
            dueDate: @event.OccurredAt.AddDays(30),
            ct: ct).ConfigureAwait(false);
    }
}
