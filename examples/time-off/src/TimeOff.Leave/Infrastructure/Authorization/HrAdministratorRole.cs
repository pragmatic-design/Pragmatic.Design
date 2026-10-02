namespace TimeOff.Leave.Infrastructure.Authorization;

/// <summary>
///     Administers people, teams, allowances, kinds of absence and holidays, and sees every request; keeps
///     the personal data by the rules — the register, erasure, the audit trail's integrity. An HR
///     administrator is an employee too, and asks for leave like one; deciding requests stays with
///     managers.
/// </summary>
[Role("hr-administrator", "Administers employees, teams, allowances, kinds of absence and holidays")]
[IncludesRole<EmployeeRole>]
[Grants(
    LeavePermissions.Employee.All,
    LeavePermissions.Team.All,
    LeavePermissions.AbsenceKind.All,
    LeavePermissions.Allowance.All,
    LeavePermissions.CompanyHoliday.All,
    LeavePermissions.LeaveRequest.ViewAll,
    LeavePermissions.PersonalData.Erase,
    LeavePermissions.ProcessingRegister.Read,
    LeavePermissions.AuditTrail.Verify,
    LeavePermissions.SecurityIncidents.Scan)]
public sealed partial class HrAdministratorRole;
