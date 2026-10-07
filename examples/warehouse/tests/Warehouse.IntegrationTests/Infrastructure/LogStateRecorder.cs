using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.CallSites;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     Records, for each entry whose message mentions a marker, whether its state came from a Pragmatic
///     generated call site.
/// </summary>
/// <remarks>
///     Added to a running host's logger factory, so it sees what the host's own loggers write. Only entries
///     naming the marker are kept, so the hosts the suite shares do not accumulate everything they log.
/// </remarks>
internal sealed class LogStateRecorder(string marker) : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedEntry> _entries = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, marker, _entries);

    /// <summary>Every entry recorded so far, oldest first.</summary>
    public IReadOnlyCollection<RecordedEntry> Entries => _entries.ToArray();

    public void Dispose()
    {
    }

    /// <summary>One entry: the category, the rendered message, and whether its state is a generated call site's.</summary>
    internal sealed record RecordedEntry(string Category, string Message, bool FromGeneratedCallSite);

    private sealed class Logger(string category, string marker, ConcurrentQueue<RecordedEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains(marker, StringComparison.Ordinal))
                entries.Enqueue(new RecordedEntry(category, message, state is IUtf8LogState));
        }
    }
}
