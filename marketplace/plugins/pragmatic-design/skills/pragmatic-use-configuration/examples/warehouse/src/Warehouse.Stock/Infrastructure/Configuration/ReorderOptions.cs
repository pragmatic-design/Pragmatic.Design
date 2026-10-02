namespace Warehouse.Stock.Infrastructure.Configuration;

/// <summary>
///     Below how many units on hand a product needs reordering.
/// </summary>
/// <remarks>
///     Changed at runtime through the Agent (<c>config/Warehouse:DefaultReorderThreshold</c>) and read
///     through <c>IOptionsMonitor</c>, so both Stock instances answer the new threshold without a restart.
/// </remarks>
[Configuration(SectionPath = "Warehouse")]
public partial class ReorderOptions
{
    /// <summary>A product with fewer units on hand than this, across every location, is to reorder.</summary>
    [Range(0, 1_000_000)]
    public int DefaultReorderThreshold { get; set; } = 5;
}
