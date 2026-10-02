namespace Showcase.Booking.Events;

/// <summary>Raised when a reservation is cancelled. Billing voids the invoice on it.</summary>
[PublicEvent]
public sealed record ReservationCancelled(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    string Reason,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
