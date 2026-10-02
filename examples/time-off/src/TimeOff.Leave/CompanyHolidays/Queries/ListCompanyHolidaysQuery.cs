namespace TimeOff.Leave.CompanyHolidays.Queries;

/// <summary>
///     The company holidays in a period, by date — every employee reads them: the permission is in
///     every role.
/// </summary>
[Query<CompanyHoliday, CompanyHolidayDto>(Paged = true)]
[RequirePermission(LeavePermissions.CompanyHoliday.Read)]
[Endpoint(HttpVerb.Get, "api/company-holidays")]
public partial class ListCompanyHolidaysQuery
{
    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Date")]
    public DateOnly? From { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Date")]
    public DateOnly? To { get; init; }

    [Sort(DefaultDirection = SortDirection.Ascending)]
    public SortDirection? DateSort { get; init; }
}
