namespace Showcase.Booking.Events;

public sealed record GuestCheckedIn(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    // Nullable: the auto-raise ([RaisesEvent] on the enum) binds this to entity.CheckedInBy (string?).
    string? CheckedInBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
