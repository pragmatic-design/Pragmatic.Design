using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A stay at a property: its check-in a time of day, its arrival a day, its length a duration, each with an optional twin left empty until
///     written; its batch keeps one run's stays apart from another's. <c>TimeOfDayTests</c> writes, reads, filters and sorts it.
/// </summary>
[Entity]
public partial class Stay : IEntity
{
    public string Batch { get; set; } = "";

    public string Code { get; set; } = "";

    public TimeOnly CheckIn { get; set; }

    public TimeOnly? LateCheckOut { get; set; }

    public DateOnly Arrival { get; set; }

    public DateOnly? Departure { get; set; }

    public TimeSpan Length { get; set; }

    public TimeSpan? Grace { get; set; }
}
