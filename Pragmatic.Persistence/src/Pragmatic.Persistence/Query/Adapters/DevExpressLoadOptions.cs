namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     DevExpress-compatible load options model.
/// </summary>
public sealed class DevExpressLoadOptions
{
    /// <summary>
    ///     The filter array in DevExpress format.
    /// </summary>
    public IReadOnlyList<object?>? Filter { get; init; }

    /// <summary>
    ///     The sort descriptors.
    /// </summary>
    public IReadOnlyList<DevExpressSortDescriptor>? Sort { get; init; }

    /// <summary>
    ///     The number of items to skip.
    /// </summary>
    public int? Skip { get; init; }

    /// <summary>
    ///     The number of items to take.
    /// </summary>
    public int? Take { get; init; }

    /// <summary>
    ///     Whether to require total count.
    /// </summary>
    public bool RequireTotalCount { get; init; }

    /// <summary>
    ///     Whether to require group count.
    /// </summary>
    public bool RequireGroupCount { get; init; }
}
