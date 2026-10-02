namespace TimeOff.Leave.Employees.Queries;

/// <summary>
///     One employee, by id.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[RecordAccess]</c> because this is somebody reading <b>another person's</b> record.
///         Writes are recorded on every path — the trail is written by an interceptor over
///         <c>SaveChanges</c> — and reads are recorded nowhere, which is the right amount for most
///         operations and not for this one: "who looked at this employee" cannot be answered afterwards,
///         the entry either was written at the time or does not exist.
///     </para>
///     <para>
///         What is recorded is the operation, the actor and the time. Not the row: an access record
///         holding the data it accounts for is a second copy of what it exists to protect.
///     </para>
///     <para>
///         <see cref="GetMyProfileQuery" /> deliberately carries none. Reading your own record is not an
///         access anybody has to answer for, and recording it would bury the reads that matter under one
///         entry per person per page load.
///     </para>
/// </remarks>
[Query<Employee, EmployeeDto>(Single = true)]
[RequirePermission(LeavePermissions.Employee.Read)]
[RecordAccess]
[Endpoint(HttpVerb.Get, "api/employees/{id}")]
public partial class GetEmployeeQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
