namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Options for bulk insert operations.
/// </summary>
public sealed class BulkInsertOptions
{
    /// <summary>Maximum number of rows per INSERT batch. Default 1000. Must be &gt; 0.</summary>
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
}
