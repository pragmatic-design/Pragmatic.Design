using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Showcase.Billing.Events;
using Showcase.Booking;
using Showcase.Booking.Reservations.Mutations;

namespace Showcase.Billing.Infrastructure.EventHandlers;

/// <summary>
/// When an invoice is paid, notifies Booking to mark the reservation as payment received.
/// Demonstrates: Cross-boundary communication via IBookingActions boundary interface.
/// Billing never references Booking entities or repositories — only the typed boundary facade.
/// [MessageHandler] with [Retry] ensures transient failures don't lose the notification.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <c>[CircuitBreaker]</c> is <c>[Retry]</c>'s companion, and the reason it is here is the
/// distributed topology: <c>IBookingActions</c> is a local call in one host and an HTTP call in the
/// other. Retry alone, against a Booking that is down, means four attempts and three and a half
/// seconds of waiting <b>per message</b> — for every message in the queue. The breaker is what
/// stops that.
/// </para>
/// <para>
/// ⚠️ <b>A failure is an attempt, not a message.</b> The generated pipeline records one for each
/// retry, so with <c>MaxAttempts = 3</c> the first message that exhausts its retries already
/// reaches the threshold of three and opens the circuit. That is the intent — the dependency is
/// down, not flaky — but it is not what "three failures" reads like, so it is written here.
/// </para>
/// </remarks>
[MessageHandler]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.Exponential, BaseDelayMs = 500)]
[CircuitBreaker(FailureThreshold = 3, BreakDurationSeconds = 30)]
public sealed partial class InvoicePaidHandler(
    IBookingActions bookingActions) : IMessageHandler<InvoicePaid>
{
    public async Task HandleAsync(InvoicePaid @event, MessageContext context, CancellationToken ct = default)
    {
        var mutation = new MarkPaymentReceivedMutation
        {
            Id = @event.ReservationId
        };

        await bookingActions.Reservations.MarkPaymentReceived(mutation, ct).ConfigureAwait(false);
    }
}
