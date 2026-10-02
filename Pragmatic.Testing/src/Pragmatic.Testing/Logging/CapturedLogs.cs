using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Testing.Logging;

/// <summary>
///     Collects what an application logged, so a test can assert on it.
/// </summary>
/// <remarks>
///     <para>
///         The framework requires structured logging through <c>[LoggerMessage]</c>, and this is how a
///         test verifies it. A rule with no signal is a convention: a log line nobody asserts on can
///         stop naming the right thing and nobody notices — a delegated write has two parties, and a
///         line naming one of them cannot answer "who did this".
///     </para>
///     <para>
///         <b>The structured state is kept, not only the rendered sentence.</b> The value of
///         <c>[LoggerMessage]</c> is the named properties; a test matching formatted text stays green
///         while the property carrying the meaning disappears.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var logs = new CapturedLogs();
/// builder.ConfigureLogging(l => l.AddProvider(logs));
/// // …
/// logs.PropertyOf("Story", "ActorId").Should().Be("agent-7");
///     </code>
/// </example>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLine> _lines = new();

    /// <summary>Every line written since the provider was attached, in order.</summary>
    public IReadOnlyCollection<CapturedLine> Lines => _lines;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Collector(categoryName, _lines);

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>Forgets everything captured so far.</summary>
    /// <remarks>
    ///     For a fixture shared across tests: without it an assertion can pass on a line another test
    ///     produced, which is the quietest way for a suite to stop meaning anything.
    /// </remarks>
    public void Clear()
    {
        while (_lines.TryDequeue(out _)) { }
    }

    /// <summary>
    ///     The value of a structured property, from the most recent line that both matches
    ///     <paramref name="message" /> and actually carries that property.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Requiring the property, and not only the text, is what makes this correct. Matching text
    ///     alone picks up neighbouring lines that happen to contain the same word — an action's own
    ///     "Action WriteStoryAction succeeded" contains <c>Story</c> — and returns <c>null</c>, which
    ///     reads exactly like "the application never logged it". A loose matcher in a test helper
    ///     produces false negatives that look like real defects.
    /// </remarks>
    /// <param name="message">A fragment of the rendered line, to pick the one you mean.</param>
    /// <param name="property">The structured property name, as written in the message template.</param>
    /// <returns>The value, or <c>null</c> when no line matches both.</returns>
    public string? PropertyOf(string message, string property) =>
        _lines
            .LastOrDefault(l => l.Text.Contains(message, StringComparison.Ordinal)
                                && l.State.Any(kv => kv.Key == property))
            ?.State.First(kv => kv.Key == property).Value?.ToString();

    /// <summary>Whether any line at the given level matches <paramref name="message" />.</summary>
    public bool Contains(LogLevel level, string message) =>
        _lines.Any(l => l.Level == level && l.Text.Contains(message, StringComparison.Ordinal));

    /// <summary>One captured line: what was written, at what level, with which properties.</summary>
    /// <param name="Category">The logger category, usually the declaring type.</param>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Text">The rendered message.</param>
    /// <param name="State">The structured properties, when the state carried any.</param>
    /// <param name="Exception">The exception, when one was logged.</param>
    public sealed record CapturedLine(
        string Category,
        LogLevel Level,
        string Text,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        Exception? Exception);

    private sealed class Collector(string category, ConcurrentQueue<CapturedLine> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>
        ///     Always on: filtering is the host's business, and a helper that quietly dropped a level
        ///     would make a test fail for a reason with nothing to do with the code under test.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            sink.Enqueue(new CapturedLine(
                category, logLevel, formatter(state, exception), properties, exception));
        }
    }
}
