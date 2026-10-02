using Pragmatic.Identity.Local.Permissions;

namespace TimeOff.Leave.Infrastructure.Authorization;

/// <summary>
///     Everyone who works here: asks for leave, reads and withdraws their own requests, reads the kinds
///     of absence and the company's holidays, and sees their own profile and balances.
/// </summary>
/// <remarks>
///     Reading a request is a permission everyone holds; which requests is the row filter's answer —
///     an employee's own, and a manager's team's too.
/// </remarks>
[Role("employee", "Asks for leave, reads and withdraws their own requests")]
[Grants(
    LocalIdentityPermissions.ChangePassword,
    LeavePermissions.OwnProfile.Manage,
    LeavePermissions.AbsenceKind.Read,
    LeavePermissions.CompanyHoliday.Read,
    LeavePermissions.LeaveRequest.Create,
    LeavePermissions.LeaveRequest.Read,
    LeavePermissions.LeaveRequest.Withdraw)]
public sealed partial class EmployeeRole;
