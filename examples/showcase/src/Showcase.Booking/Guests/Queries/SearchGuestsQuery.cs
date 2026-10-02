namespace Showcase.Booking.Guests.Queries;

/// <summary>
/// Paged search for guests.
/// Demonstrates: [Query] + [Endpoint] with Contains filter for text search, and Paged = true,
/// which writes the two paging properties instead of repeating them here.
/// Enables guest lookup by email or last name — autocomplete-friendly.
/// </summary>
[Query<Guest, GuestDto>(Paged = true)]
[RequirePermission(BookingPermissions.Guest.Read)]
[Endpoint(HttpVerb.Get, "api/guests/search")]
public partial class SearchGuestsQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? Email { get; init; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? LastName { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? LastNameSort { get; init; }
}
