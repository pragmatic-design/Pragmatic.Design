using Pragmatic.Temporal.Attributes;

namespace Showcase.Booking.Dtos;

/// <summary>
/// Reservation summary for list views.
/// Demonstrates: MapFrom, GenerateProjection, MapProperty (navigation flattening), GenerateBodyOnlyVariant.
/// </summary>
[MapFrom<Reservation>]
[GenerateProjection]
[GenerateBodyOnlyVariant]
public partial class ReservationSummaryDto
{
    public Guid Id { get; init; }
    public Guid GuestId { get; init; }
    public Guid PropertyId { get; init; }
    /// <summary>Read in the zone the caller asked for; stored, as always, in UTC.</summary>
    [ToClientTimezone]
    public DateTimeOffset CheckIn { get; init; }

    /// <summary>Deliberately bare: the control that the conversion is this attribute's doing.</summary>
    public DateTimeOffset CheckOut { get; init; }

    /// <summary>
    /// Read on the hotel's clock, whatever zone the caller is in — the front desk is who acts on it.
    /// This is the attribute CheckIn's is not: [ToClientTimezone] follows the reader, this one does
    /// not move when the reader does.
    /// </summary>
    [ToBusinessTimezone]
    public DateTimeOffset? ExpectedArrival { get; init; }

    /// <summary>
    /// When the guest actually walked in, on the hotel's clock — the same wall clock the front desk
    /// typed at check-in, whoever is reading it now. See CheckInGuestMutation.ActualArrival, which is
    /// [FromBusinessTimezone]: the pair is what makes the round trip an identity.
    /// </summary>
    [ToBusinessTimezone]
    public DateTimeOffset? ActualArrival { get; init; }

    public int NumberOfGuests { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "";
    public ReservationStatus Status { get; init; }

    /// <summary>Flattened from Reservation.Property.Name.</summary>
    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";

    /// <summary>Flattened from Reservation.Guest.FirstName.</summary>
    [MapProperty("Guest.FirstName")]
    public string GuestFirstName { get; init; } = "";

    /// <summary>Flattened from Reservation.Guest.LastName.</summary>
    [MapProperty("Guest.LastName")]
    public string GuestLastName { get; init; } = "";

    /// <summary>
    /// Localized status label for the current culture.
    /// Demonstrates: T.xxx embedded translation keys (generated from translations/*.json).
    /// </summary>
    [MapIgnore]
    public string StatusLabel => Status switch
    {
        ReservationStatus.Pending => T.Reservation.Status.Pending.Value,
        ReservationStatus.Confirmed => T.Reservation.Status.Confirmed.Value,
        ReservationStatus.PaymentReceived => T.Reservation.Status.PaymentReceived.Value,
        ReservationStatus.CheckedIn => T.Reservation.Status.CheckedIn.Value,
        ReservationStatus.CheckedOut => T.Reservation.Status.CheckedOut.Value,
        ReservationStatus.Cancelled => T.Reservation.Status.Cancelled.Value,
        ReservationStatus.NoShow => T.Reservation.Status.NoShow.Value,
        _ => Status.ToString()
    };
}
