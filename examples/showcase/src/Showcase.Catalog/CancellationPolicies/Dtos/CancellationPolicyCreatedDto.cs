namespace Showcase.Catalog.Dtos;

/// <summary>
/// A cancellation policy as the create endpoint answers with it.
/// </summary>
/// <remarks>
/// Same fields as <see cref="CancellationPolicyDto"/> minus the flattened <c>Property.Name</c>, for
/// the reason spelled out on <see cref="RoomTypeCreatedDto"/>: the entity a write hands back has no
/// navigations loaded.
/// </remarks>
[MapFrom<CancellationPolicy>]
public partial class CancellationPolicyCreatedDto
{
    public Guid Id { get; init; }
    public Guid PropertyId { get; init; }
    public string Name { get; init; } = "";
    public int HoursBeforeCheckIn { get; init; }
    public decimal PenaltyPercentage { get; init; }
    public bool IsDefault { get; init; }
}
