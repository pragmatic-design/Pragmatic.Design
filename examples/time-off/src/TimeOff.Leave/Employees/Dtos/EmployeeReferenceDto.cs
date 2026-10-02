namespace TimeOff.Leave.Dtos;

/// <summary>An employee as another answer names them: the number and the name.</summary>
[MapFrom<Employee>]
public partial class EmployeeReferenceDto
{
    public Guid Id { get; init; }

    public string EmployeeNumber { get; init; } = "";

    public string FullName { get; init; } = "";
}
