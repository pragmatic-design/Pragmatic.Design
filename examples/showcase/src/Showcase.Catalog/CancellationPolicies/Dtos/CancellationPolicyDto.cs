namespace Showcase.Catalog.Dtos;

/// <summary>
/// Cancellation policy DTO for display.
/// Demonstrates: MapFrom, GenerateProjection, MapProperty (flattening navigation).
/// </summary>
[MapFrom<CancellationPolicy>]
[GenerateProjection]
public partial class CancellationPolicyDto
{
    public Guid Id { get; init; }
    public Guid PropertyId { get; init; }
    public string Name { get; init; } = "";
    public int HoursBeforeCheckIn { get; init; }
    public decimal PenaltyPercentage { get; init; }
    public bool IsDefault { get; init; }

    /// <summary>Flattened from CancellationPolicy.Property.Name.</summary>
    [MapProperty("Property.Name")]
    public string PropertyName { get; init; } = "";
}
