namespace Showcase.Booking.Events;

/// <summary>Raised when a reservation is created. Billing raises the draft invoice on it.</summary>
[PublicEvent]
public sealed record ReservationCreated(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    Guid RoomTypeId,
    DateTimeOffset CheckIn,
    DateTimeOffset CheckOut,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
