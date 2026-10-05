namespace Pragmatic.Endpoints.Benchmarks.Documents;

/// <summary>
///     The shape of Showcase's <c>ReservationSummaryDto</c>, the item of a reservation list response.
/// </summary>
/// <remarks>
///     A copy of the members that reach the wire, not a reference to the Showcase module: that would bring
///     the persistence and mapping pipeline into a serialization benchmark. Two members are left out on
///     purpose. <c>Status</c> is an enum, which the host writes by name through a converter, and neither
///     RE:Dox nor STJ's fast path writes a name under the same contract, so the work would not be equal.
///     <c>StatusLabel</c> is computed from it.
/// </remarks>
public sealed class ReservationSummary
{
    public Guid Id { get; set; }
    public Guid GuestId { get; set; }
    public Guid PropertyId { get; set; }
    public DateTimeOffset CheckIn { get; set; }
    public DateTimeOffset CheckOut { get; set; }
    public DateTimeOffset? ExpectedArrival { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public int NumberOfGuests { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public string GuestFirstName { get; set; } = "";
    public string GuestLastName { get; set; } = "";
}
