namespace TimeOff.Leave.Errors;

/// <summary>
///     Personal data is erased after the employment ends, not during it: an employee who has not left
///     cannot be erased.
/// </summary>
/// <remarks>The words are in <c>translations/*.json</c>, under <c>error.employee.still.active</c>.</remarks>
public sealed partial record EmployeeStillActiveError : Error
{
    public override string Code => "EMPLOYEE_STILL_ACTIVE";
    public override int StatusCode => 409;
}
