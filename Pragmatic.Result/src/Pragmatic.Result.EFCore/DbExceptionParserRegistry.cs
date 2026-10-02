namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Composes the database exception parsers used to classify exceptions into typed
///     <see cref="DbErrorInfo" />.
/// </summary>
/// <remarks>
///     <para>
///         Provider-specific parsers (registered by the provider packages via
///         <c>Add{Provider}ResultErrorHandling</c>) are tried first, in registration order; the
///         universal <see cref="HeuristicDbExceptionParser" /> is always tried last as a fallback.
///     </para>
///     <para>
///         The parser set is fixed at construction, so the registry is immutable and safe to share
///         across threads with no locking on the read path. When resolved from dependency injection
///         it is composed from <em>every</em> registered <see cref="IDbExceptionParser" />, so
///         multiple provider packages coexist rather than the first-registered one winning.
///     </para>
/// </remarks>
public sealed class DbExceptionParserRegistry
{
    // Frozen at construction: provider-specific parsers first, heuristic fallback last.
    // Immutable => thread-safe reads without a lock.
    private readonly IDbExceptionParser[] _parsers;

    /// <summary>
    ///     Initializes a registry that uses only the heuristic fallback parser.
    /// </summary>
    public DbExceptionParserRegistry()
        => _parsers = [HeuristicDbExceptionParser.Instance];

    /// <summary>
    ///     Initializes a registry composed from the supplied parsers.
    /// </summary>
    /// <param name="parsers">
    ///     The provider-specific parsers, typically resolved from dependency injection. They are
    ///     ordered provider-specific-first; the heuristic fallback is always appended last. Any
    ///     explicit <see cref="HeuristicDbExceptionParser" /> in the sequence is dropped to avoid a
    ///     duplicate — it is always present as the terminal fallback.
    /// </param>
    public DbExceptionParserRegistry(IEnumerable<IDbExceptionParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);

        var providerParsers = parsers.Where(p => p is not HeuristicDbExceptionParser);
        _parsers = [.. providerParsers, HeuristicDbExceptionParser.Instance];
    }

    /// <summary>
    ///     Gets the default registry, which uses only heuristic parsing (no provider parsers).
    /// </summary>
    public static DbExceptionParserRegistry Default { get; } = new();

    /// <summary>
    ///     Gets the provider names of the composed parsers, in the order they are tried
    ///     (provider-specific first, <c>"Heuristic"</c> last).
    /// </summary>
    public IReadOnlyList<string> RegisteredProviders
        => [.. _parsers.Select(p => p.ProviderName)];

    /// <summary>
    ///     Parses an exception using the first parser that recognizes it and produces a confident
    ///     classification, falling through to the heuristic parser otherwise.
    /// </summary>
    /// <param name="exception">The exception to parse.</param>
    /// <returns>
    ///     Parsed error information. When a parser recognizes the exception type but cannot map the
    ///     specific code, the result carries <see cref="DbErrorType.Unknown" /> with the original
    ///     message as details.
    /// </returns>
    public DbErrorInfo Parse(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        DbErrorInfo? fallback = null;
        foreach (var parser in _parsers)
        {
            if (!parser.CanParse(exception))
                continue;

            var result = parser.Parse(exception);
            if (result is null)
                continue;

            // A confident classification wins immediately. An Unknown means the parser recognized the
            // exception type but not the specific code — remember it and let the next parser
            // (ultimately the heuristic) try for something more specific.
            if (result.Value.ErrorType is not DbErrorType.Unknown)
                return result.Value;

            fallback ??= result;
        }

        return fallback ?? new DbErrorInfo { ErrorType = DbErrorType.Unknown, Details = exception.Message };
    }
}
