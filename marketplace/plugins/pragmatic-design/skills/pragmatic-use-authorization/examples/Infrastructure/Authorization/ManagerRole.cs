namespace TimeOff.Leave.Infrastructure.Authorization;

/// <summary>
///     An employee who also decides the requests of the teams they manage. Which teams is data, not a
///     permission: the permission lets a manager decide, the request's team decides whose.
/// </summary>
[Role("manager", "Decides the leave requests of the teams they manage")]
[IncludesRole<EmployeeRole>]
[Grants(LeavePermissions.LeaveRequest.Decide)]
public sealed partial class ManagerRole;
