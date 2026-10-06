using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     A provider that knows nothing of Pragmatic, reading an entry the usual way: the formatter's text and
///     the state as a list of pairs.
/// </summary>
internal sealed class ThirdPartyProvider : ILoggerProvider
{
    public List<string> Written { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(Written);

    public void Dispose()
    {
    }

    private sealed class Logger(List<string> written) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IReadOnlyList<KeyValuePair<string, object?>> list
                ? string.Join(";", list.Select(p => $"{p.Key}={p.Value}"))
                : "";
            written.Add(formatter(state, exception) + " | " + pairs);
        }
    }
}
