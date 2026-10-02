namespace Showcase.Catalog.Entities;

/// <summary>
/// Cancellation policy for a hotel property, defining penalty windows.
/// Demonstrates: [Autocomplete] on Name for search-as-you-type.
/// </summary>
[Entity]
[Auditable]
[Relation.ManyToOne<Property>]
public partial class CancellationPolicy : IEntity
{
    [Autocomplete]
    public string Name { get; private set; } = "";

    /// <summary>How many hours before check-in the guest can cancel without penalty.</summary>
    public int HoursBeforeCheckIn { get; private set; } = 48;

    /// <summary>Penalty as a percentage of total (0-100).</summary>
    public decimal PenaltyPercentage { get; private set; }

    public bool IsDefault { get; private set; }

}
