namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     PrimeNG-compatible lazy load event model.
/// </summary>
public sealed class PrimeNGLazyLoadEvent
{
    /// <summary>
    ///     First row offset.
    /// </summary>
    public int First { get; init; }

    /// <summary>
    ///     Number of rows to load.
    /// </summary>
    public int Rows { get; init; }

    /// <summary>
    ///     Sort field for single-column sorting.
    /// </summary>
    public string? SortField { get; init; }

    /// <summary>
    ///     Sort order: 1 = ascending, -1 = descending.
    /// </summary>
    public int SortOrder { get; init; } = 1;

    /// <summary>
    ///     Multi-sort metadata for multi-column sorting.
    /// </summary>
    public IReadOnlyList<PrimeNGSortMeta>? MultiSortMeta { get; init; }

    /// <summary>
    ///     Column filters.
    /// </summary>
    public IReadOnlyDictionary<string, PrimeNGFilterMetadata>? Filters { get; init; }

    /// <summary>
    ///     Global filter value.
    /// </summary>
    public string? GlobalFilter { get; init; }

    /// <summary>
    ///     Fields to apply global filter to.
    /// </summary>
    public IReadOnlyList<string>? GlobalFilterFields { get; init; }
}
