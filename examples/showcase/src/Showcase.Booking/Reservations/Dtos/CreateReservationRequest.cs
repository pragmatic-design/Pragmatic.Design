using Pragmatic.Temporal.Attributes;

namespace Showcase.Booking.Dtos;

/// <summary>
/// Request to create a reservation.
/// Demonstrates: FutureDate, GreaterThanProperty, Positive, Range validation attributes.
/// Note: [Required] omitted on value types (Guid, DateTimeOffset) — they cannot be null.
/// </summary>
public partial class CreateReservationRequest
{
    public Guid GuestId { get; init; }

    public Guid PropertyId { get; init; }

    public Guid RoomTypeId { get; init; }

    [FutureDate]
    public DateTimeOffset CheckIn { get; init; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; init; }

    /// <summary>
    /// What time the guest expects to arrive, as they typed it on their own device: a wall clock with
    /// no offset, e.g. <c>2026-05-04T21:30:00</c>. [FromClientTimezone] reads it in the zone the caller
    /// declared (X-Timezone) and the stored value is the instant that means.
    /// </summary>
    /// <remarks>
    /// ⚠️ DateTime and not DateTimeOffset, on purpose. A DateTimeOffset carries its own offset, so the
    /// instant is already decided and [FromClientTimezone] has nothing left to decide — it would be
    /// indistinguishable from no attribute at all, which is how a feature gets a test that passes for
    /// the wrong reason.
    /// </remarks>
    [FromClientTimezone]
    public DateTime? ExpectedArrival { get; init; }

    [Positive]
    [Range(1, 20)]
    public int NumberOfGuests { get; init; } = 1;

    public string? SpecialRequests { get; init; }
}
