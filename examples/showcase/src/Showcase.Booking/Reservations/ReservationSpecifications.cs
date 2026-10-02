namespace Showcase.Booking.Entities;

/// <summary>
/// Composable specification factory for reservations.
/// Demonstrates: Spec&lt;T&gt;.Where(), operator composition, AndIf/OrIf for dynamic filters.
/// </summary>
/// <remarks>
///     The other half of the generated <c>ReservationSpecifications</c>, which holds <c>ById</c>: one class per
///     entity, in the entities' namespace, so every file that sees <see cref="Reservation" /> sees its rules too.
///     The generator also turns each member into a read of its own — <c>.IsActive()</c> on the queryable,
///     <c>FindIsActiveAsync()</c> on the repository.
/// </remarks>
public static partial class ReservationSpecifications
{
    /// <summary>Not cancelled and not soft-deleted.</summary>
    public static Specification<Reservation> IsActive()
        => Spec<Reservation>.Where(r => r.Status != ReservationStatus.Cancelled && !r.IsDeleted);

    /// <summary>Reservations for a specific guest.</summary>
    public static Specification<Reservation> ForGuest(Guid guestId)
        => Spec<Reservation>.Where(r => r.GuestId == guestId);

    /// <summary>Reservations at a specific property.</summary>
    public static Specification<Reservation> AtProperty(Guid propertyId)
        => Spec<Reservation>.Where(r => r.PropertyId == propertyId);

    /// <summary>Reservations that overlap a date range at a property.</summary>
    public static Specification<Reservation> Overlapping(
        Guid propertyId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut)
        => IsActive()
           & AtProperty(propertyId)
           & Spec<Reservation>.Where(r => r.CheckIn < checkOut && r.CheckOut > checkIn);

    /// <summary>Reservations that overlap a date range for a specific room type at a property.</summary>
    public static Specification<Reservation> Overlapping(
        Guid propertyId,
        Guid roomTypeId,
        DateTimeOffset checkIn,
        DateTimeOffset checkOut)
        => Overlapping(propertyId, checkIn, checkOut)
           & Spec<Reservation>.Where(r => r.RoomTypeId == roomTypeId);

    /// <summary>Reservations with a specific status.</summary>
    public static Specification<Reservation> WithStatus(ReservationStatus status)
        => Spec<Reservation>.Where(r => r.Status == status);

    /// <summary>Confirmed reservations.</summary>
    /// <remarks>
    ///     <para>
    ///         Demonstrates the promotion: <c>[Query]</c> derives <c>IsConfirmedQuery</c> from this rule,
    ///         and <c>[Endpoint]</c> gives that derived query a route. The rule is written once and is
    ///         both composable — <c>IsActive() &amp; IsConfirmed()</c> — and readable over HTTP.
    ///     </para>
    ///     <para>
    ///         ⚠️ The route is <b>opt-in</b>: the other specifications in this class declare none and get
    ///         none. A rule is not an operation until somebody says where it answers.
    ///     </para>
    /// </remarks>
    [Query<Reservation, ReservationSummaryDto>]
    [RequirePermission(BookingPermissions.Reservation.Read)]
    [Endpoint(HttpVerb.Get, "api/reservations/confirmed")]
    public static Specification<Reservation> IsConfirmed()
        => WithStatus(ReservationStatus.Confirmed);

    /// <summary>Pending reservations.</summary>
    public static Specification<Reservation> IsPending()
        => WithStatus(ReservationStatus.Pending);

    /// <summary>
    /// Builds a dynamic search spec from optional filters.
    /// Demonstrates conditional composition with AndIf/OrIf.
    /// </summary>
    public static Specification<Reservation> Search(
        Guid? guestId = null,
        Guid? propertyId = null,
        ReservationStatus? status = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        return IsActive()
            .AndIf(guestId.HasValue, ForGuest(guestId ?? Guid.Empty))
            .AndIf(propertyId.HasValue, AtProperty(propertyId ?? Guid.Empty))
            .AndIf(status.HasValue, WithStatus(status ?? ReservationStatus.Pending))
            .AndIf(fromDate.HasValue, Spec<Reservation>.Where(r => r.CheckIn >= fromDate!.Value))
            .AndIf(toDate.HasValue, Spec<Reservation>.Where(r => r.CheckOut <= toDate!.Value));
    }
}
