namespace TimeOff.Leave.Reports.Queries;

/// <summary>
///     One line of the absence report: a team, a kind of absence and a month, with what was taken in it.
/// </summary>
/// <remarks>
///     <para>
///         The grouping is declared here and computed by the database: one <c>GROUP BY</c> over the
///         requests, the amounts summed and the requests counted, nothing loaded to be added up.
///     </para>
///     <para>
///         The team is the one the request was asked in, not the employee's current one. The month is the
///         month the request starts in (<see cref="LeaveRequest.StartMonth" />): a request across two months
///         counts whole in the first. <see cref="Taken" /> is in the kind's <see cref="Unit" /> — days, or
///         hours for a kind counted in hours — so the unit is part of the line.
///     </para>
/// </remarks>
[QueryView<LeaveRequest>]
[GroupBy<LeaveRequest>(Properties = "TeamId,AbsenceKindId,StartMonth")]
[GroupBy<AbsenceKind>(Properties = "Unit", Via = "AbsenceKind")]
public partial class AbsenceReportLine
{
    /// <summary>The team the requests were asked in; null for an employee who was in none.</summary>
    public Guid? TeamId { get; init; }

    public Guid AbsenceKindId { get; init; }

    /// <summary>The month, 1–12, the requests start in.</summary>
    public int StartMonth { get; init; }

    /// <summary>What <see cref="Taken" /> counts: days or hours.</summary>
    public AbsenceUnit Unit { get; init; }

    /// <summary>The approved amount, in <see cref="Unit" />.</summary>
    [Sum<LeaveRequest>(Expression = "Amount")]
    public decimal Taken { get; init; }

    /// <summary>How many approved requests make it up.</summary>
    [Count<LeaveRequest>]
    public int Requests { get; init; }
}
