namespace TimeOff.Leave.Dtos;

/// <summary>
///     A company holiday, named in the reader's language.
/// </summary>
[MapFrom<CompanyHoliday>]
[GenerateProjection]
public partial class CompanyHolidayDto
{
    public Guid Id { get; init; }

    public DateOnly Date { get; init; }

    public string Name { get; init; } = "";

    public CompanyHolidayKind Kind { get; init; }
}
