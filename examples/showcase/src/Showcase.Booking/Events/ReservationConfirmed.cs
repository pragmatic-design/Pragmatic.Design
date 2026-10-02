namespace Showcase.Booking.Events;

/// <summary>
/// Raised when a reservation is confirmed.
/// Carries all data needed by downstream consumers (e.g., Billing)
/// so they don't need cross-boundary entity access.
/// </summary>
/// <remarks>
/// <c>[PublicEvent]</c> is that sentence said where a consumer can read it: the generated AsyncAPI
/// document is the only place the shape and the channel of this event are published, and without the
/// marker the document catalogues a Booking event as internal — including the three Billing handles. The marker changes no behaviour; it changes what the contract says, which is the only
/// thing a consumer has.
/// </remarks>
[PublicEvent]
public sealed record ReservationConfirmed(
    Guid ReservationId,
    Guid GuestId,
    Guid PropertyId,
    DateTimeOffset CheckIn,
    DateTimeOffset CheckOut,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
