namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     DevExpress sort descriptor.
/// </summary>
public sealed class DevExpressSortDescriptor
{
    /// <summary>
    ///     The property selector.
    /// </summary>
    public string? Selector { get; init; }

    /// <summary>
    ///     Whether to sort descending.
    /// </summary>
    public bool Desc { get; init; }
}
