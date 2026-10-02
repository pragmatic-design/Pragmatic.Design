namespace Showcase.Booking.Dtos;

/// <summary>
/// A reservation as a list card shows it — the one read in the Showcase that maps in memory.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[MapFrom]</c> without <c>[GenerateProjection]</c>, deliberately. <see cref="Card" />
///         is assembled from three fields after the row arrives, which is not a shape an
///         <c>Expression</c> can carry into SQL — so the query declares <c>MapInMemory = true</c> and
///         the executor runs the generated <c>Selector</c> over the rows it was going to return
///         anyway. Filtering, sorting and paging stay server-side; only the projection moves.
///     </para>
///     <para>
///         ⚠️ And that is exactly why <c>[EagerLoad("Guest")]</c> on the query is not an optimisation
///         here. The mapper reads <c>Reservation.Guest</c> <b>in memory</b>: without the include the
///         navigation is empty and the guest's name comes back blank. A <c>MapInMemory</c> query
///         emits its <c>IncludePaths</c>, which is why this DTO and that query go together.
///     </para>
/// </remarks>
[MapFrom<Reservation>]
public partial class ReservationCardDto
{
    public Guid Id { get; init; }

    public string ReservationNumber { get; init; } = "";

    /// <summary>Flattened from Reservation.Guest.FirstName — read through the navigation, in memory.</summary>
    [MapProperty("Guest.FirstName")]
    public string GuestFirstName { get; init; } = "";

    /// <summary>Flattened from Reservation.Guest.LastName.</summary>
    [MapProperty("Guest.LastName")]
    public string GuestLastName { get; init; } = "";

    public int NightsCount { get; init; }

    /// <summary>
    ///     What the card shows, assembled after the row arrives.
    /// </summary>
    /// <remarks>
    ///     The reason this type does not project: a <c>Select</c> would have to build this string in
    ///     SQL, and the invariant formatting below is the kind of thing that then differs by provider
    ///     and by collation. It costs nothing in memory, over rows the page was returning anyway.
    /// </remarks>
    [MapIgnore]
    public string Card => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{GuestLastName}, {GuestFirstName} · {ReservationNumber} · {NightsCount} nights");
}
