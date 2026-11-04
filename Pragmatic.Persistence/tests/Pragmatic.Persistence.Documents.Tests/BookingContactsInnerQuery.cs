using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>The bookings whose phone starts with a prefix, with their guest's email: an inner join by key (#508), a booking whose guest is not there dropped.</summary>
[Query<Booking, BookingContact>]
[Join<Guest>(ForeignKey = "GuestId", TargetKey = "Id", Type = JoinType.Inner, Alias = "Who")]
public partial class BookingContactsInnerQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Phone { get; init; }

    [Sort(MapTo = nameof(Booking.Nights), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NightsSort { get; init; }
}
