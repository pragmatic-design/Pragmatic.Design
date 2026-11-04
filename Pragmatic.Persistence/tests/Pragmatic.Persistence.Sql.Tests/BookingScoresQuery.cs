using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The bookings of a guest with their score, a <c>[Projectable]</c> member of the entity the projection reads (#506), by nights.</summary>
[Query<Booking, BookingScore>]
public partial class BookingScoresQuery
{
    public Guid? GuestId { get; init; }

    [Sort(MapTo = nameof(Booking.Nights), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NightsSort { get; init; }
}
