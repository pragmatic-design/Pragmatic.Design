using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Everything the application wrote to its logs, as a provider would have formatted it.
/// </summary>
/// <remarks>
///     <para>
///         Registered like any other provider, on purpose. Declared redaction works by replacing the
///         <c>ILoggerFactory</c> in the container, so the guarantee is that <em>whatever</em> provider is
///         present is handed values that are already masked — and a provider added by a test host is
///         exactly the case that has to hold for the guarantee to be measurable at all.
///     </para>
///     <para>
///         ⚠️ It keeps the rendered message, not the state. The mask is applied to the value before any
///         provider formats it, so what a test must read is what a provider would have written; reading
///         the state object back would inspect the argument the caller passed and prove nothing.
///     </para>
/// </remarks>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>Every entry written since the host started, in the order the providers saw them.</summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(formatter(state, exception));
        }
    }
}
