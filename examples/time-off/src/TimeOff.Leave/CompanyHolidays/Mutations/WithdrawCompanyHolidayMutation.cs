namespace TimeOff.Leave.CompanyHolidays.Mutations;

/// <summary>
///     HR takes back a company holiday. Requests already made keep the days they counted.
/// </summary>
[Mutation(Mode = MutationMode.Delete)]
[RequirePermission(LeavePermissions.CompanyHoliday.Delete)]
[Endpoint(HttpVerb.Delete, "api/company-holidays/{id}")]
public partial class WithdrawCompanyHolidayMutation : Mutation<CompanyHoliday>
{
    public required Guid Id { get; init; }
}
