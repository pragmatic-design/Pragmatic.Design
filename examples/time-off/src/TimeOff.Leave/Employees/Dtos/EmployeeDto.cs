namespace TimeOff.Leave.Dtos;

/// <summary>
///     An employee as HR and the employee see them. The credentials never leave the server.
/// </summary>
[MapFrom<Employee>]
[GenerateProjection]
public partial class EmployeeDto
{
    public Guid Id { get; init; }

    public string EmployeeNumber { get; init; } = "";

    public string FullName { get; init; } = "";

    public string WorkEmail { get; init; } = "";

    public AccessRole Role { get; init; }

    public DateOnly HiredOn { get; init; }

    public Guid? TeamId { get; init; }

    public string? PreferredCulture { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string? CreatedBy { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }
}
