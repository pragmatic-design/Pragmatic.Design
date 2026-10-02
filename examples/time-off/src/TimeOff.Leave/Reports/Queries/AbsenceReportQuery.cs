namespace TimeOff.Leave.Reports.Queries;

/// <summary>
///     The days of absence of a year, by team, kind and month — for staffing, and for the payroll.
/// </summary>
/// <remarks>
///     HR only: it reads every request (<c>view-all</c>). Only approved requests count. The query filters;
///     <see cref="AbsenceReportLine" /> groups.
/// </remarks>
[Query<LeaveRequest, AbsenceReportLine>]
[RequirePermission(LeavePermissions.LeaveRequest.ViewAll)]
[Endpoint(HttpVerb.Get, "api/reports/absences")]
public partial class AbsenceReportQuery
{
    /// <summary>The year the requests start in.</summary>
    [BindSpecification]
    public required int Year { get; init; }

    public Specification<LeaveRequest> InTheYear => LeaveRequestSpecifications.StartingIn(Year);

    /// <summary>Absences taken, not asked for: a pending or rejected request is no day away.</summary>
    public static Specification<LeaveRequest> Approved => LeaveRequestSpecifications.Approved;
}
