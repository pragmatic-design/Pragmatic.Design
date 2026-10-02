namespace TimeOff.Leave.Enums;

/// <summary>
///     What an employee may do in Time off. HR assigns it; the sign-in puts the role each one signs in as in the
///     token — <c>employee.Role.RoleNameOf()</c>, generated from the <c>[SignsInAs]</c> below — and the host maps
///     each role to its permissions.
/// </summary>
/// <remarks>
///     A new member without <c>[SignsInAs]</c> is a build error (PRAG1014): the mapping is exhaustive, where the
///     hand-written switch it replaces threw at the sign-in of whoever had the member it forgot.
/// </remarks>
public enum AccessRole
{
    /// <summary>Asks for leave and sees their own requests and balance.</summary>
    [SignsInAs<EmployeeRole>]
    Employee,

    /// <summary>An employee who also decides the requests of the teams they manage.</summary>
    [SignsInAs<ManagerRole>]
    Manager,

    /// <summary>Administers people, teams, allowances, kinds of absence and holidays.</summary>
    [SignsInAs<HrAdministratorRole>]
    HrAdministrator
}
