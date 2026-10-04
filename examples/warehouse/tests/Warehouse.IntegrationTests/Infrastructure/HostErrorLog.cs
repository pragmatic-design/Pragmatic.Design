using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The errors one host logged, kept so an assertion can carry them.
/// </summary>
/// <remarks>
///     A 500 says only that something threw: the body of the problem response carries no exception, and
///     a test host's log reaches no console. A failure that does not reproduce on a re-run is diagnosable
///     only from what the host logged while it happened, so the assertion that saw the status brings it.
/// </remarks>
internal sealed class HostErrorLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    /// <summary>Every error logged so far, oldest first, for an assertion about one of them.</summary>
    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    /// <summary>
    ///     Every error logged so far, newest first, each with its chain of exceptions and the frame that threw
    ///     each one.
    /// </summary>
    /// <remarks>
    ///     Newest first and without full stacks because the gate keeps forty lines of a failure: the error
    ///     that answered the request is the last one logged, and a start-up error before it with forty
    ///     frames would push it out.
    /// </remarks>
    public override string ToString() => _entries.IsEmpty
        ? "(the host logged no error)"
        : string.Join(Environment.NewLine, _entries.Reverse());

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            entries.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{Chain(exception)}");
        }

        private static string Chain(Exception? exception)
        {
            var lines = new List<string>();
            for (var current = exception; current is not null; current = current.InnerException)
            {
                var thrownAt = current.StackTrace?.Split('\n', 2)[0].Trim();
                lines.Add($"  {current.GetType().FullName}: {current.Message}{(thrownAt is null ? "" : $" ({thrownAt})")}");
            }

            return lines.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, lines);
        }
    }
}
