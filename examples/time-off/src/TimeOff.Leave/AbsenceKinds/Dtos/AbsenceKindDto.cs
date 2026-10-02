namespace TimeOff.Leave.Dtos;

/// <summary>
///     A kind of absence as a reader sees it: the name in their language, and every translation for
///     whoever edits it.
/// </summary>
[MapFrom<AbsenceKind>]
[GenerateProjection]
public partial class AbsenceKindDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = "";

    /// <summary>In the culture of the request.</summary>
    public string Name { get; init; } = "";

    /// <summary>Every translation HR wrote.</summary>
    [MapProperty("Name")]
    public LocalizedString? Names { get; init; }

    public AbsenceUnit Unit { get; init; }

    public bool UsesAllowance { get; init; }
}
