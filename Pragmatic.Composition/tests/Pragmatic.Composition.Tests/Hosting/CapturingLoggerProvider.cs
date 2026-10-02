using Microsoft.Extensions.Logging;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     Records every log entry written through it, so a test can assert on what a host said.
///     One instance is both the provider and the logger: the tests need the entries, not the categories.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
                return [.. _entries];
        }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
            _entries.Add((logLevel, formatter(state, exception)));
    }

    public void Dispose()
    {
    }
}
