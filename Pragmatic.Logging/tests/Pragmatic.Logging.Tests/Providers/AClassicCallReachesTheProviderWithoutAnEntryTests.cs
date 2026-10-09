using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A classic call — <c>logger.LogInformation(template, args)</c> — reaches a provider as the call it is: its
///     properties read from the call's state, no <see cref="LogEntry" /> and no dictionary built for it.
/// </summary>
public class AClassicCallReachesTheProviderWithoutAnEntryTests
{
    private const string Template = "Order {OrderId} processed for user {UserId}";

    /// <summary>
    ///     With the state and its message already built, every byte a call allocates is the path's own: the
    ///     entry, its dictionary, a list, an enumerator. There are none.
    /// </summary>
    /// <remarks>
    ///     A state of MEL's — <c>LogInformation(template, args)</c> — is a struct, and reading it without boxing
    ///     takes optimized code, which a Debug build never runs; that call is measured by the benchmarks, in
    ///     Release. The state here is a class, so the measure holds in every configuration.
    /// </remarks>
    [Fact]
    public void AtSteadyState_TheWritePathAllocatesNothing()
    {
        var sink = new ReadingProvider(Configuration(context: false));
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var logger = factory.CreateLogger("Orders");
        var state = new OrderState(67890, "user12345");

        for (var i = 0; i < 200; i++)
            logger.Log(LogLevel.Information, default, state, null, OrderState.Format);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            logger.Log(LogLevel.Information, default, state, null, OrderState.Format);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0);
        sink.Last.Should().Be("Order 67890 processed for user user12345 | OrderId=67890 UserId=user12345");
        sink.LastTemplate.Should().Be(Template);
    }

    [Fact]
    public void TheProviderReadsTheTemplateTheScopesAndTheException()
    {
        var sink = new ReadingProvider(Configuration(context: false));
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var logger = factory.CreateLogger("Orders");
        var failure = new InvalidOperationException("declined");

        using (logger.BeginScope(new Dictionary<string, object?> { ["SessionId"] = "s-1" }))
            logger.LogError(failure, Template, 1, "u");

        sink.Last.Should().Be("Order 1 processed for user u | OrderId=1 UserId=u | scope SessionId=s-1 | InvalidOperationException");
        sink.LastTemplate.Should().Be(Template);
    }

    [Fact]
    public void TheContextIsReadIntoTheProperties()
    {
        var sink = new ReadingProvider(Configuration(context: true));
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var logger = factory.CreateLogger("Orders");

        using (LogContextScope.PushContext())
        {
            LogContextScope.Current!.SetProperty("CorrelationId", "c-7");
            logger.LogInformation(Template, 1, "u");
        }

        sink.Last.Should().Contain("CorrelationId=c-7");
    }

    private static PragmaticProviderConfiguration Configuration(bool context)
    {
        var configuration = PragmaticNullConfiguration.ForStructuredBenchmarking();
        configuration.MinimumLevel = LogLevel.Information;
        configuration.IncludeContextEnrichment = context;
        if (context)
        {
            configuration.ContextFilter.Mode = ContextFilterMode.Include;
            configuration.ContextFilter.PropertyNames = ["CorrelationId"];
        }

        return configuration;
    }

    // A call's state as MEL's is shaped — the pairs, then the template — built once, with its message.
    private sealed class OrderState(int orderId, string userId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly KeyValuePair<string, object?>[] _pairs =
        [
            new("OrderId", orderId),
            new("UserId", userId),
            new("{OriginalFormat}", Template),
        ];

        private readonly string _message = $"Order {orderId} processed for user {userId}";

        public static readonly Func<OrderState, Exception?, string> Format = static (state, _) => state._message;

        public int Count => _pairs.Length;

        public KeyValuePair<string, object?> this[int index] => _pairs[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_pairs).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // Reads what a writing provider reads, into a reused buffer; keeps only the last line.
    private sealed class ReadingProvider(IPragmaticProviderConfiguration configuration)
        : PragmaticLoggerProviderBase("reading", configuration)
    {
        private readonly StringBuilder _line = new(256);
        private string _last = "";

        public string Last => _last;

        public string? LastTemplate { get; private set; }

        protected override void WriteLogCore(LogEvent logEvent)
        {
            _line.Clear().Append(logEvent.Message).Append(" |");
            foreach (var property in logEvent.Properties)
                Append(property);

            if (logEvent.Scopes.Count > 0)
            {
                _line.Append(" | scope");
                foreach (var scope in logEvent.Scopes)
                    Append(scope);
            }

            if (logEvent.Exception is { } exception)
                _line.Append(" | ").Append(exception.GetType().Name);

            LastTemplate = logEvent.MessageTemplate;

            // Only when it changed, so the steady state allocates nothing here.
            if (!_line.Equals(_last.AsSpan()))
                _last = _line.ToString();
        }

        // Formatted into the builder, not through ToString(): the provider must not be what allocates.
        private void Append(KeyValuePair<string, object?> property)
            => _line.Append(' ').Append(property.Key).Append('=')
                .Append(CultureInfo.InvariantCulture, $"{property.Value ?? "null"}");
    }
}
