namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The reservation book by status, every status included: a status no reservation is in answers its row, with zero.
/// Demonstrates: [GroupBy(IncludeEmpty = true)] on a single enum key (#766).
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ReservationsByStatusView" /> answers the groups the rows form, as a <c>GROUP BY</c> does. This one adds a row for each
///         status none of the rows is in, after the others: a dashboard that draws a bar per status no longer joins the answer to the enum
///         itself.
///     </para>
///     <para>
///         ⚠️ <see cref="LargestBooking" /> is nullable, and has to be: a group of no reservations has no largest one, and a non-nullable
///         <c>decimal</c> would answer 0, which nobody could tell from a real 0 (PRAG0750). The count and the sum of an empty group are 0.
///     </para>
/// </remarks>
[QueryView<Reservation>]
[GroupBy<Reservation>(Properties = "Status", IncludeEmpty = true)]
public partial class ReservationsByEveryStatusView
{
    /// <summary>The status this row counts.</summary>
    [From<Reservation>]
    public ReservationStatus Status { get; init; }

    /// <summary>How many reservations are in it; 0 for a status none is in.</summary>
    [Count<Reservation>]
    public int Reservations { get; init; }

    /// <summary>What they are worth together; 0 for a status none is in.</summary>
    [Sum<Reservation>(Expression = "TotalAmount")]
    public decimal TotalAmount { get; init; }

    /// <summary>The largest single booking of the group; null for a status none is in.</summary>
    [Max<Reservation>(Expression = "TotalAmount")]
    public decimal? LargestBooking { get; init; }
}
