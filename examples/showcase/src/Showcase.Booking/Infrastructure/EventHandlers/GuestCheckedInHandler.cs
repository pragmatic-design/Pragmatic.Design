using Microsoft.Extensions.Logging;

namespace Showcase.Booking.Infrastructure.EventHandlers;

/// <summary>
/// Handles the GuestCheckedIn event within the Booking boundary.
/// Demonstrates intra-boundary event handling for observability and audit.
/// </summary>
[EventHandler]
public sealed partial class GuestCheckedInHandler(ILogger<GuestCheckedInHandler> logger)
    : IDomainEventHandler<GuestCheckedIn>
{
    public Task HandleAsync(GuestCheckedIn @event, CancellationToken ct = default)
    {
        LogGuestCheckedIn(@event.GuestId, @event.ReservationId, @event.CheckedInBy ?? "system", @event.OccurredAt);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Guest {GuestId} checked in to reservation {ReservationId} by {CheckedInBy} at {OccurredAt}")]
    private partial void LogGuestCheckedIn(Guid guestId, Guid reservationId, string checkedInBy, DateTimeOffset occurredAt);
}
