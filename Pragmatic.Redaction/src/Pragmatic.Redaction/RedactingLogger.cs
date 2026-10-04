using Microsoft.Extensions.Logging;

namespace Pragmatic.Redaction;

/// <summary>
///     Masks the declared members in a log entry's structured values, and renders the message from
///     the masked values, before handing both to the logger underneath.
/// </summary>
internal sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly DeclaredRedactor _redactor;

    public RedactingLogger(ILogger inner, DeclaredRedactor redactor)
    {
        _inner = inner;
        _redactor = redactor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Only a structured state can be masked member by member. A plain string state carries no
        // declared type, so there is nothing to look up and nothing to mask.
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values
            || _redactor.RedactState(values) is not { } redacted)
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        // The original formatter closes over the ORIGINAL state, so using it here would restore the
        // unmasked value in the message text. Render from the masked values instead.
        _inner.Log(logLevel, eventId, redacted, exception, static (s, _) => s.ToString()!);
    }
}
