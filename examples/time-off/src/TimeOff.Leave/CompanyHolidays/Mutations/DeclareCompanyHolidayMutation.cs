namespace TimeOff.Leave.CompanyHolidays.Mutations;

/// <summary>
///     HR declares a day the company is closed: a closure, or the patron saint. From then on requests
///     that cover it do not count it.
/// </summary>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(LeavePermissions.CompanyHoliday.Create)]
[Endpoint(HttpVerb.Post, "api/company-holidays")]
[CreatedAt("/api/company-holidays/{Id}")]
[ReturnsDto<CompanyHolidayDto>]
public partial class DeclareCompanyHolidayMutation : Mutation<CompanyHoliday>
{
    public required DateOnly Date { get; init; }

    public required LocalizedString Name { get; init; }

    public required CompanyHolidayKind Kind { get; init; }
}
