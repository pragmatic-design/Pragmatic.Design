namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Options for upsert (insert-or-update) operations.
/// </summary>
public sealed class UpsertOptions
{
    /// <summary>Maximum number of rows per batch. Default 1000. Must be &gt; 0.</summary>
    public int BatchSize
    {
        get;
        // A non-positive batch size makes the batching loop never advance (infinite loop).
        set => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "BatchSize must be greater than zero.");
    } = 1000;

    /// <summary>Command timeout in seconds. Default 30.</summary>
    public int CommandTimeout { get; set; } = 30;

    /// <summary>
    ///     Which columns to match on for determining if entity exists.
    ///     Default is PrimaryKey.
    /// </summary>
    public UpsertMatch MatchOn { get; set; } = UpsertMatch.PrimaryKey;

    /// <summary>
    ///     When true, adds RowVersion to the UPSERT match condition so that
    ///     rows with a stale RowVersion are skipped (not updated).
    ///     Requires the entity to be marked <c>[ConcurrencyAware]</c>.
    ///     Default is false.
    /// </summary>
    public bool ConcurrencyCheck { get; set; }
}
