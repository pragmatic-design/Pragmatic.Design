using Pragmatic.Persistence.Entity;

namespace PragmaticDocumentSamples.Entities;

/// <summary>A stretch of work: [TemporalRelation], read at the caller's instant.</summary>
[Entity]
[TemporalRelation]
public partial class StaffAssignment : IEntity, ITemporalRelation
{
    public string Person { get; set; } = "";

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }
}
