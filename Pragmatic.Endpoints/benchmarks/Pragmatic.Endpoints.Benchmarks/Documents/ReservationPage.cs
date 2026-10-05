namespace Pragmatic.Endpoints.Benchmarks.Documents;

/// <summary>A page of reservation summaries, as a list endpoint answers it.</summary>
public sealed class ReservationPage
{
    public List<ReservationSummary> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }

    /// <summary>
    ///     A page of fifty, deterministic so every run serializes the same bytes: half the arrivals unset,
    ///     which the host's null handling leaves out, and names with non-ASCII characters to escape.
    /// </summary>
    public static ReservationPage Sample()
    {
        var start = new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.FromHours(2));
        var items = new List<ReservationSummary>(50);
        for (var i = 0; i < 50; i++)
        {
            items.Add(new ReservationSummary
            {
                Id = new Guid(i, 1, 1, [1, 2, 3, 4, 5, 6, 7, 8]),
                GuestId = new Guid(i, 2, 2, [1, 2, 3, 4, 5, 6, 7, 8]),
                PropertyId = new Guid(i % 5, 3, 3, [1, 2, 3, 4, 5, 6, 7, 8]),
                CheckIn = start.AddDays(i),
                CheckOut = start.AddDays(i + 3),
                ExpectedArrival = i % 2 == 0 ? start.AddDays(i).AddHours(2) : null,
                ActualArrival = i % 4 == 0 ? start.AddDays(i).AddHours(3) : null,
                NumberOfGuests = 1 + i % 4,
                TotalAmount = 120.50m + i * 17.25m,
                Currency = "EUR",
                PropertyName = i % 3 == 0 ? "Hôtel du Lac" : "Grand Hotel Roma",
                GuestFirstName = i % 2 == 0 ? "Zoë" : "Marco",
                GuestLastName = "Rossi-" + i,
            });
        }

        return new ReservationPage { Items = items, Page = 1, PageSize = 50, TotalCount = 1234 };
    }
}
