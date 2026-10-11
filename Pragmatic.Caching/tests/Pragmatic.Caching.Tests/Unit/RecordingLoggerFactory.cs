using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Every entry any of its loggers writes, at every level, with its id, message, state and exception.
/// </summary>
/// <remarks>
///     A factory rather than a provider: this project references only the logging abstractions, and
///     <see cref="Logger{T}" /> over this factory is what the cache stack is handed.
/// </remarks>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<RecordedLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(RecordingLoggerFactory factory) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IReadOnlyList<KeyValuePair<string, object?>> list ? list.ToArray() : [];
            factory.Entries.Enqueue(new RecordedLogEntry(logLevel, eventId, formatter(state, exception), pairs, exception));
        }
    }
}
