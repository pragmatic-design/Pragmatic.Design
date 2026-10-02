namespace Showcase.Catalog.Dtos;

/// <summary>
/// Request body for importing a single room type's seasonal rate.
/// </summary>
public sealed record SeasonalRateImportRequest
{
    public Guid RoomTypeId { get; init; }
    public decimal NewBaseRate { get; init; }
}
