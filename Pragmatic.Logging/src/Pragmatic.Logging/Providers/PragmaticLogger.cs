using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Providers;

/// <summary>
/// Logger implementation that works with PragmaticLoggerProvider infrastructure.
/// </summary>
internal sealed class PragmaticLogger(string categoryName, PragmaticLoggerProviderBase provider) : ILogger
{
    private readonly string _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
    private readonly PragmaticLoggerProviderBase _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return LoggerExternalScopeProvider.CreateScope(state);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel logLevel)
    {
        return _provider.IsEnabled(_categoryName, logLevel);
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        ArgumentNullException.ThrowIfNull(formatter);

        // Deferred fast path: when no pipeline feature needs a materialized LogEntry, hand the
        // typed state straight to the sink — no LogEntry, no dictionaries, no eager rendering.
        if (_provider.TryWriteDeferred(logLevel, eventId, state, exception, formatter, _categoryName))
            return;

        // Declared redaction reaches the message as well as the properties. The caller's formatter
        // closes over the original state, so a value whose type declared members has to be masked
        // here, before the text is rendered: masking only LogEntry.Properties afterwards left the
        // member in clear in every line a provider wrote.
        if (_provider.DeclaredRedactor is { IsEmpty: false } redactor
            && state is IReadOnlyList<KeyValuePair<string, object?>> values
            && redactor.RedactState(values) is { } redacted)
        {
            WriteEntry(logLevel, eventId, redacted, exception, redacted.ToString()!);
            return;
        }

        WriteEntry(logLevel, eventId, state, exception, formatter(state, exception));
    }

    private void WriteEntry<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, string message)
    {
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        // Timestamp is set by the LogEntry constructor — don't call DateTime.UtcNow twice.
        var logEntry = new LogEntry
        {
            LogLevel = logLevel,
            EventId = eventId,
            Category = _categoryName,
            Message = message,
            Exception = exception
        };

        // Extract structured properties only when the target provider consumes them —
        // for message-only providers this skips the per-call dictionary entirely.
        if (_provider.Configuration.IncludeStructuredProperties)
        {
            ExtractStructuredProperties(state, logEntry);
        }

        // Capture scope information (no-op without an active scope)
        CaptureScopes(logEntry);

        _provider.WriteLog(logEntry);
    }

    private static void ExtractStructuredProperties<TState>(TState state, LogEntry logEntry)
    {
        // Handle different state types that might contain structured data
        switch (state)
        {
            case IReadOnlyList<KeyValuePair<string, object?>> keyValueList:
                // This is the most common case for structured logging
                foreach (var kvp in keyValueList)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Key != "{OriginalFormat}")
                    {
                        logEntry.Properties[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Key == "{OriginalFormat}")
                    {
                        logEntry.MessageTemplate = kvp.Value?.ToString();
                    }
                }
                break;

            case IEnumerable<KeyValuePair<string, object?>> keyValueEnumerable:
                foreach (var kvp in keyValueEnumerable)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Key != "{OriginalFormat}")
                    {
                        logEntry.Properties[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Key == "{OriginalFormat}")
                    {
                        logEntry.MessageTemplate = kvp.Value?.ToString();
                    }
                }
                break;

                // Note: ILogMessage handling will be added when the message types are fully implemented
        }
    }

    private static void CaptureScopes(LogEntry logEntry)
    {
        // The common case is "no active scope": bail out before allocating anything.
        if (!LoggerExternalScopeProvider.HasActiveScopes)
            return;

        var scopes = new List<KeyValuePair<string, object?>>();

        LoggerExternalScopeProvider.ForEachScope((scope, state) =>
        {
            if (scope is IReadOnlyList<KeyValuePair<string, object?>> scopeKeyValues)
            {
                scopes.AddRange(scopeKeyValues);
            }
            else if (scope is IEnumerable<KeyValuePair<string, object?>> scopeEnumerable)
            {
                scopes.AddRange(scopeEnumerable);
            }
            else if (scope != null)
            {
                // For simple scope values, use the scope value as both key and value
                scopes.Add(new KeyValuePair<string, object?>("Scope", scope));
            }
        }, (object?)null);

        if (scopes.Count > 0)
        {
            logEntry.Scopes = scopes;
        }
    }
}

/// <summary>
/// Simple external scope provider for managing logging scopes.
/// </summary>
internal static class LoggerExternalScopeProvider
{
    private static readonly AsyncLocal<Scope?> _currentScope = new();

    /// <summary>Whether any scope is active on the current async flow (allocation-free check).</summary>
    public static bool HasActiveScopes => _currentScope.Value != null;

    public static IDisposable? CreateScope<T>(T state) where T : notnull
    {
        if (state == null)
            return null;

        return new Scope(state);
    }

    public static void ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
        var current = _currentScope.Value;
        while (current != null)
        {
            callback(current.State, state);
            current = current.Parent;
        }
    }

    // The scope node doubles as the caller's IDisposable: one allocation per BeginScope.
    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public Scope(object state)
        {
            State = state;
            Parent = _currentScope.Value;
            _currentScope.Value = this;
        }

        public Scope? Parent { get; }
        public object State { get; }

        public void Dispose()
        {
            if (!_disposed)
            {
                _currentScope.Value = Parent;
                _disposed = true;
            }
        }
    }
}
