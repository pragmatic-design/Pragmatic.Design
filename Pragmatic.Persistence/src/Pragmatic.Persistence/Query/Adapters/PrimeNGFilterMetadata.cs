namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     PrimeNG filter metadata.
/// </summary>
public sealed class PrimeNGFilterMetadata
{
    /// <summary>
    ///     The filter value.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    ///     The match mode (equals, contains, startsWith, etc.).
    /// </summary>
    public string? MatchMode { get; init; }

    /// <summary>
    ///     The operator for combining with other filters on same field.
    /// </summary>
    public string? Operator { get; init; }
}
