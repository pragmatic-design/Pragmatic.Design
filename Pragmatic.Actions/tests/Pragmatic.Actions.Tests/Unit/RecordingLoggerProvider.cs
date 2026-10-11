using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>Every entry any logger of the factory writes, at every level, with its id, message and state.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<RecordedLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IReadOnlyList<KeyValuePair<string, object?>> list ? list.ToArray() : [];
            provider.Entries.Enqueue(new RecordedLogEntry(logLevel, eventId, formatter(state, exception), pairs));
        }
    }
}
