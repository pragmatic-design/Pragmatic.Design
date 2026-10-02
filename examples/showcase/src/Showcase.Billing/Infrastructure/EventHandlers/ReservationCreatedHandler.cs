using Microsoft.Extensions.Logging;
using Showcase.Billing.Infrastructure.Services;
using Showcase.Booking.Events;

namespace Showcase.Billing.Infrastructure.EventHandlers;

/// <summary>
/// Reacts to <see cref="ReservationCreated" /> from the Booking boundary.
/// Performs billing pre-validation (credit check, fraud detection) <em>before</em>
/// the reservation is confirmed — so the confirmation flow can reject ineligible reservations early.
/// </summary>
/// <remarks>
/// Cross-boundary event flow:
/// <list type="number">
///   <item><description><b>ReservationCreated</b> → this handler: pre-validate billing eligibility</description></item>
///   <item><description><b>ReservationConfirmed</b> → ReservationConfirmedHandler: create the actual invoice</description></item>
///   <item><description><b>ReservationCancelled</b> → ReservationCancelledHandler: void the invoice</description></item>
/// </list>
/// Demonstrates: cross-boundary event handling, DI injection in event handlers.
/// </remarks>
[EventHandler]
public sealed partial class ReservationCreatedHandler(
    IBillingEligibilityService eligibilityService,
    ILogger<ReservationCreatedHandler> logger)
    : IDomainEventHandler<ReservationCreated>
{
    public async Task HandleAsync(ReservationCreated @event, CancellationToken ct = default)
    {
        LogReservationCreated(@event.ReservationId, @event.GuestId, @event.TotalAmount, @event.Currency);

        var isEligible = await eligibilityService.IsEligibleAsync(
            @event.GuestId, @event.TotalAmount, @event.Currency, ct).ConfigureAwait(false);

        if (!isEligible)
        {
            // In production: publish a BillingEligibilityFailed event so the Booking boundary
            // can cancel the reservation before it reaches confirmation.
            LogEligibilityFailed(@event.ReservationId, @event.GuestId, @event.TotalAmount, @event.Currency);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reservation {ReservationId} created for guest {GuestId}: {TotalAmount} {Currency}. Running billing pre-check.")]
    private partial void LogReservationCreated(Guid reservationId, Guid guestId, decimal totalAmount, string currency);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Billing eligibility FAILED for reservation {ReservationId}, guest {GuestId}: {TotalAmount} {Currency}. Reservation should be cancelled.")]
    private partial void LogEligibilityFailed(Guid reservationId, Guid guestId, decimal totalAmount, string currency);
}
