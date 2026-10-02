namespace TimeOff.Leave.Dtos;

/// <summary>
///     A team and who manages it.
/// </summary>
[MapFrom<Team>]
[GenerateProjection]
public partial class TeamDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public Guid ManagerId { get; init; }
}
