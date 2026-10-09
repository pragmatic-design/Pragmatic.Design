using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
///     A classic call written from its state: the event read where the call put it, no <see cref="LogEntry" /> and no
///     dictionary (<see cref="LogEvent" />).
/// </summary>
public abstract partial class PragmaticLoggerProviderBase
{
    // One event per thread, reused from call to call; a provider that logs while it writes gets another.
    [ThreadStatic] private static LogEvent? t_event;

    private static LogEvent RentEvent()
    {
        var logEvent = t_event;
        if (logEvent is null || logEvent.InUse)
        {
            logEvent = new LogEvent();
            t_event ??= logEvent;
        }

        logEvent.InUse = true;
        return logEvent;
    }

    private static void ReturnEvent(LogEvent logEvent) => logEvent.Release();

    /// <summary>
    ///     Writes the call as <see cref="WriteLogCore" /> receives it, read from its state: the message rendered, the
    ///     properties copied out of the state, the scopes from the ambient stack, the context when it is enriched.
    ///     False when there was nothing to write, as the entry path decides: no message and no exception.
    /// </summary>
    /// <remarks>
    ///     The same event the entry path builds, field for field, without the entry: the properties in the order
    ///     the state lists them, <c>{OriginalFormat}</c> taken as the template; the context written over them as
    ///     the entry's dictionary would; the scopes innermost first. Processing time is not sampled on this path, as
    ///     on the deferred one: it exists to do no per-call bookkeeping.
    /// </remarks>
    private bool WriteState<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter, string category)
    {
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return false;

        var logEvent = RentEvent();
        try
        {
            logEvent.Start(DateTime.UtcNow, logLevel, eventId, category, message, template: null, exception);

            if (_configuration.IncludeStructuredProperties)
                ReadProperties(state, logEvent);

            ReadScopes(logEvent.Scopes);

            if (EnrichesWithContext)
                EnrichWithContext(logEvent.Properties);

            WriteLogCore(logEvent);
            return true;
        }
        finally
        {
            ReturnEvent(logEvent);
        }
    }

    // As the entry path extracts them: every pair with a key, {OriginalFormat} as the template.
    // Optimized from the first call: unoptimized code boxes a struct state on every cast below (MEL's
    // FormattedLogValues is one), five boxes a call until tiering catches up. For a struct state the
    // instantiation is specialized and the call already direct, so skipping the profiled tier costs nothing.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ReadProperties<TState>(TState state, LogEvent logEvent)
    {
        // The interface is called on the cast, never held: a struct state is not boxed for it.
        if (state is IReadOnlyList<KeyValuePair<string, object?>>)
        {
            var count = ((IReadOnlyList<KeyValuePair<string, object?>>)state).Count;
            for (var i = 0; i < count; i++)
                Read(((IReadOnlyList<KeyValuePair<string, object?>>)state)[i], logEvent);
        }
        else if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
                Read(pair, logEvent);
        }
    }

    private static void Read(KeyValuePair<string, object?> pair, LogEvent logEvent)
    {
        if (pair.Key == "{OriginalFormat}")
            logEvent.SetTemplate(pair.Value?.ToString());
        else if (!string.IsNullOrEmpty(pair.Key))
            logEvent.Properties[pair.Key] = pair.Value;
    }

    // As the entry path captures them: each scope's pairs, innermost scope first; a bare value under "Scope".
    private static void ReadScopes(LogEventValues scopes)
    {
        if (!LoggerExternalScopeProvider.HasActiveScopes)
            return;

        LoggerExternalScopeProvider.ForEachScope(static (scope, target) =>
        {
            switch (scope)
            {
                case IReadOnlyList<KeyValuePair<string, object?>> list:
                    for (var i = 0; i < list.Count; i++)
                        target.Append(list[i].Key, list[i].Value);
                    break;
                case IEnumerable<KeyValuePair<string, object?>> pairs:
                    foreach (var pair in pairs)
                        target.Append(pair.Key, pair.Value);
                    break;
                case not null:
                    target.Append("Scope", scope);
                    break;
            }
        }, scopes);
    }
}
