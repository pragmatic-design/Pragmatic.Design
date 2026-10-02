namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     PrimeNG sort metadata.
/// </summary>
public sealed class PrimeNGSortMeta
{
    /// <summary>
    ///     The field to sort by.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    ///     Sort order: 1 = ascending, -1 = descending.
    /// </summary>
    public int Order { get; init; } = 1;
}
