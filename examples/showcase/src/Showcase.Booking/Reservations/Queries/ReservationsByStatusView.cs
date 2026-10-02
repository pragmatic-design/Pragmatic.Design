namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The reservation book counted by status, in one round trip.
/// Demonstrates: [QueryView] + [GroupBy] + aggregates as the result type of a [Query].
/// </summary>
/// <remarks>
///     <para>
///         The grouping is declared here, beside the aggregates it feeds, and the query that exposes
///         it declares nothing about it: <c>[Query&lt;Reservation, ReservationsByStatusView&gt;]</c>
///         filters and the view groups. That is the whole point of the pairing — an aggregate read is
///         a read of the same kind, with a route, a permission and the invoker.
///     </para>
///     <para>
///         ⚠️ It counts in the database. A dashboard that fetched the rows to count them is the
///         difference between a header that renders and one that times out on a busy property.
///     </para>
/// </remarks>
[QueryView<Reservation>]
[GroupBy<Reservation>(Properties = "Status")]
public partial class ReservationsByStatusView
{
    /// <summary>The status this row counts.</summary>
    [From<Reservation>]
    public ReservationStatus Status { get; init; }

    /// <summary>How many reservations are in it.</summary>
    [Count<Reservation>]
    public int Reservations { get; init; }

    /// <summary>What they are worth together.</summary>
    [Sum<Reservation>(Expression = "TotalAmount")]
    public decimal TotalAmount { get; init; }

    /// <summary>The largest single booking of the group.</summary>
    [Max<Reservation>(Expression = "TotalAmount")]
    public decimal LargestBooking { get; init; }
}
