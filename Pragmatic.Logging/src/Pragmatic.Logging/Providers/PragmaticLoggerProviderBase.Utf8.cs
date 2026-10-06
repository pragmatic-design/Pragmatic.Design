using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;

namespace Pragmatic.Logging.Providers;

/// <summary>The path a generated call site's state takes: written by the provider, no entry built.</summary>
public abstract partial class PragmaticLoggerProviderBase
{
    /// <summary>
    ///     Whether this provider writes a generated call site's state itself, through
    ///     <see cref="WriteUtf8State{TState}" />. Providers that write synchronously override it; the
    ///     state must not escape the call.
    /// </summary>
    protected internal virtual bool SupportsUtf8State => false;

    /// <summary>
    ///     Whether a call can be written by <see cref="WriteUtf8State{TState}" />: its state writes every
    ///     argument itself, and nothing in the pipeline needs the entry materialized — the pattern
    ///     redactor works on strings, context enrichment and filters on the entry, and the ambient scopes
    ///     are written beside the properties by the classic path.
    /// </summary>
    private bool CanWriteUtf8State<TState>(IUtf8LogStateWriter<TState> writer)
        => writer.IsSelfContained
           && _dataRedactor == null
           && !_configuration.IncludeContextEnrichment
           && _configuration.Filters.Filters.Count == 0
           && !LoggerExternalScopeProvider.HasActiveScopes;

    /// <summary>
    ///     Writes a generated call site's state through the writer its type registered, without a
    ///     <see cref="LogEntry" /> and without rendering the message to a string. Only called when
    ///     <see cref="SupportsUtf8State" /> is true and the call qualifies; the caller has already checked
    ///     <see cref="IsEnabled" />.
    /// </summary>
    protected virtual void WriteUtf8State<TState>(
        LogLevel logLevel,
        EventId eventId,
        IUtf8LogStateWriter<TState> writer,
        in TState state,
        Exception? exception,
        string category)
    {
    }
}
