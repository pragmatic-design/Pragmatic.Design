using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Benchmarks.Comparison;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Redaction;

namespace Pragmatic.Logging.Benchmarks.Redaction;

/// <summary>
///     What declared redaction adds to a log call, on Pragmatic alone.
/// </summary>
/// <remarks>
///     Not a comparison row: no other library in the comparison masks members a type declared, so there
///     is nothing on their side to measure it against. The same call is timed twice, through the same
///     consuming provider, once without a redactor and once with one that knows the logged type.
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
public class RedactionOverheadBenchmarks
{
    private readonly Customer _customer = new("C-42", "jane@example.com", 7);
    private readonly List<ServiceProvider> _owned = [];

    private ILogger _plain = null!;
    private ILogger _redacting = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plain = Logger(redactor: null);
        _redacting = Logger(new DeclaredRedactor([new CustomerRedactionMap()]));

        // The row is only worth timing if the redactor actually masks: check the property it measures.
        // ⚠️ The property, not the whole entry: the rendered message still carries the member in clear
        // (#77), and that is a defect of the pipeline, not of what this row times.
        var sink = new EventConsumer { Capture = true };
        var probe = Logger(new DeclaredRedactor([new CustomerRedactionMap()]), sink);
        probe.LogInformation("Registered {Customer}", _customer);
        var consumed = sink.TakeLast();
        var property = consumed?.Properties.SingleOrDefault(p => p.StartsWith("Customer=", StringComparison.Ordinal));
        if (property is null
            || property.Contains("jane@example.com", StringComparison.Ordinal)
            || !property.Contains(PersonalDataPatterns.Mask, StringComparison.Ordinal))
            throw new InvalidOperationException($"The redacting logger did not mask the declared member: {consumed?.ToString() ?? "(nothing reached the sink)"}");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void WithoutRedaction() => _plain.LogInformation("Registered {Customer}", _customer);

    [Benchmark]
    public void WithDeclaredRedaction() => _redacting.LogInformation("Registered {Customer}", _customer);

    private ILogger Logger(DeclaredRedactor? redactor, EventConsumer? sink = null)
    {
        var configuration = PragmaticNullConfiguration.ForProductionBenchmarking();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder => builder.AddProvider(_ =>
            new PragmaticConsumingProvider("Consuming", configuration, sink ?? new EventConsumer())
            {
                DeclaredRedactor = redactor,
            }));

        var provider = services.BuildServiceProvider();
        _owned.Add(provider);
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }
}
