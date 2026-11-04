using Pragmatic.Actions.Mutation;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>The bookings whose phone starts with a prefix, as letters mapped in memory with their guest loaded (#505), by nights.</summary>
[Query<Booking, BookingLetter>(MapInMemory = true)]
[EagerLoad("Guest")]
public partial class BookingLettersQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Phone { get; init; }

    [Sort(MapTo = nameof(Booking.Nights), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NightsSort { get; init; }
}
