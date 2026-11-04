using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticDocuments.Tests;

// The Sql engine's test declarations (tests/Pragmatic.Persistence.Sql.Tests/Queries.cs), on the documents engine's entities.

/// <summary>A paged search over reviews, declared as Pragmatic declares queries: filters, a sort with its default, a page.</summary>
[Query<Review>]
public partial class SearchReviewsQuery
{
    /// <summary>A string under [Filter] without an operator: Contains, Pragmatic's default.</summary>
    [Filter]
    public string? Text { get; init; }

    /// <summary>Nullable, no attribute: an optional filter, Equals.</summary>
    public bool? Published { get; init; }

    /// <summary>A collection: In, on the field it names.</summary>
    [Filter(MapTo = nameof(Review.PropertyId))]
    public List<Guid>? Properties { get; init; }

    [Filter(Operator = FilterOperator.StartsWith, IgnoreCase = true, MapTo = nameof(Review.Text))]
    public string? Prefix { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? TextSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 2;
}

/// <summary>A review's line: a projection, the DTO's fields only.</summary>
[MapFrom<Review>]
public sealed class ReviewLine
{
    public string Text { get; init; } = "";

    public bool Published { get; init; }
}

/// <summary>A DevExpress grid over reviews: every scalar the class declares, the stars restricted to three operators.</summary>
[GridAdapter<Review>(Framework = GridFramework.DevExpress)]
[GridField("stars", AllowedOperators = [">=", "<=", "equals"])]
public partial class ReviewGrid;

/// <summary>An unpaged query into a DTO, its sort without a default (applied only when asked).</summary>
[Query<Review, ReviewLine>]
public partial class ReviewLinesQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Text { get; init; }

    [Sort]
    public SortDirection? TextSort { get; init; }
}

/// <summary>An OR group of a filter object: where a booking's phone ends, or how it starts (the Sql engine's <c>ReviewSourceFilter</c>).</summary>
[FilterDto<Booking>]
public partial class BookingPhoneFilter
{
    [Filter(Operator = FilterOperator.EndsWith, MapTo = nameof(Booking.Phone))]
    public string? At { get; set; }

    [Filter(Operator = FilterOperator.StartsWith, MapTo = nameof(Booking.Phone))]
    public string? By { get; set; }
}

/// <summary>A filter object: an OR group, and a member joined to it with AND.</summary>
[FilterDto<Booking>]
public partial class BookingFilter
{
    [FilterGroup(FilterLogic.Or)]
    public BookingPhoneFilter? Phone { get; set; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = nameof(Booking.Nights))]
    public int? MinNights { get; set; }
}

/// <summary>One guest's bookings whose filters are one [ComplexFilter], as the Showcase's property search carries its location.</summary>
[Query<Booking>]
public partial class FilterBookingsQuery
{
    public Guid? GuestId { get; init; }

    [ComplexFilter]
    public BookingFilter? Where { get; init; }

    [Sort(MapTo = nameof(Booking.Phone), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? PhoneSort { get; init; }
}

/// <summary>A booking's card: its nights, and its guest's email and name read through the navigation (<c>[MapProperty("Guest.…")]</c>).</summary>
[MapFrom<Booking>]
public sealed class BookingCard
{
    public int Nights { get; init; }

    [MapProperty("Guest.Email")]
    public string? GuestEmail { get; init; }

    [MapProperty("Guest.FullName")]
    public string? GuestName { get; init; }
}

/// <summary>The cards of the bookings whose phone starts with a prefix, by nights.</summary>
[Query<Booking, BookingCard>]
public partial class BookingCardsQuery
{
    [Filter(Operator = FilterOperator.StartsWith)]
    public string? Phone { get; init; }

    [Sort(MapTo = nameof(Booking.Nights), DefaultDirection = SortDirection.Ascending)]
    public SortDirection? NightsSort { get; init; }
}

/// <summary>A stay's card: its code under another name and its arrival, both read through <c>[MapProperty]</c> from the document's own fields.</summary>
[MapFrom<Stay>]
public sealed class StayCard
{
    [MapProperty("Code")]
    public string Label { get; init; } = "";

    [MapProperty("Arrival")]
    public DateOnly ArrivesOn { get; init; }
}

/// <summary>One batch's stay cards, sorted by arrival when asked.</summary>
[Query<Stay, StayCard>]
public partial class StayCardsQuery
{
    [Filter(Operator = FilterOperator.Equals)]
    public string? Batch { get; init; }

    [Sort(MapTo = nameof(Stay.Arrival))]
    public SortDirection? ArrivalSort { get; init; }
}
