namespace Showcase.Catalog.Dtos;

/// <summary>
/// Result of a bulk rate import operation.
/// </summary>
public sealed record ImportRatesResult
{
    public int TotalSubmitted { get; init; }
    public int TotalUpdated { get; init; }

    /// <summary>
    /// Items that were not updated due to: RoomType not found, wrong PropertyId, or invalid rate (≤ 0).
    /// </summary>
    public int TotalSkipped { get; init; }
}
