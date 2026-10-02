using Showcase.Catalog.Entities;

namespace Showcase.Booking.Entities;

/// <summary>
/// Staff member assigned to a property for a validity period.
/// Demonstrates: [TemporalRelation&lt;Property&gt;] with MaxActive = 1 (one staff per role per property at a time).
/// SG generates: Active(), ActiveAt(date), IncludeHistory(), ForProperty(), ActiveForProperty() extensions,
/// ValidateTemporalConstraints() (scoped per Property), AutoClosePrevious().
/// </summary>
[Entity]
[Relation.ManyToOne<Property>]
[TemporalRelation<Property>(MaxActive = 1)]
// The question a temporal relation is kept for: who held this role, in which order, and where the
// handovers were. [GenerateTimeline] writes the LAG/LEAD CTE that answers it — each period with the
// end of the one before and the start of the one after — partitioned by the property, so one hotel's
// handovers do not bleed into another's. Written by hand it is a window function and a raw query;
// declared, it is one line and the column names come from the EF model rather than from a guess.
[GenerateTimeline]
public partial class StaffAssignment : IEntity, ITemporalRelation
{
    public Guid StaffId { get; private set; }
    public string Role { get; private set; } = "";

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public static StaffAssignment Create(Guid staffId, Guid propertyId, string role, DateTimeOffset validFrom) =>
        new()
        {
            StaffId = staffId,
            PropertyId = propertyId,
            Role = role,
            ValidFrom = validFrom
        };
}
