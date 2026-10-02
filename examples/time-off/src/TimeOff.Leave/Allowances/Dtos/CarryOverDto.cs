namespace TimeOff.Leave.Dtos;

/// <summary>
///     What a year-end carry-over moved into the next year for one employee.
/// </summary>
public sealed record CarryOverDto(Guid EmployeeId, string EmployeeNumber, decimal CarriedOver);
