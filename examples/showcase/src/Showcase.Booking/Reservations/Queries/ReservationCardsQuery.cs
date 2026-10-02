namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The reservation list as cards — a read that maps in memory instead of projecting.
/// </summary>
/// <remarks>
///     <para>
///         The only <c>MapInMemory</c> read in the Showcase, and the first anywhere in the repository
///         that also declares a navigation to load. <c>ReservationCardDto</c> assembles its card from
///         three fields after the row arrives, which no <c>Expression</c> carries into SQL — so the
///         query says it maps, and the executor materialises the page it was returning anyway and runs
///         the generated <c>Selector</c> over it.
///     </para>
///     <para>
///         ⚠️ <c>[EagerLoad("Guest")]</c> is load-bearing, not decoration: the mapper reads
///         <c>Reservation.Guest</c> <b>in memory</b>, so without the include the navigation is empty
///         and the guest's name is blank.
///     </para>
/// </remarks>
[Query<Reservation, ReservationCardDto>(MapInMemory = true)]
[EagerLoad("Guest")]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/reservations/cards")]
public partial class ReservationCardsQuery
{
    /// <summary>Only one property's reservations, when asked.</summary>
    [Filter]
    public Guid? PropertyId { get; init; }

    /// <summary>Only one reservation number, when asked — what the integration case reads by.</summary>
    [Filter]
    public string? ReservationNumber { get; init; }
}
