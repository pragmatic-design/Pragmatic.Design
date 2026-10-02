namespace TimeOff.Leave.Dtos;

/// <summary>A kind of absence as another answer names it: the code, and the name in the language of the request.</summary>
[MapFrom<AbsenceKind>]
public partial class AbsenceKindReferenceDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    /// <summary>In the culture of the request.</summary>
    public string Name { get; init; } = "";

    public AbsenceUnit Unit { get; init; }
}
