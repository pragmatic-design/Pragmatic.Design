using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     Where the bytes and the time of a <c>[LoggerMessage]</c> call written by the JSON provider go, measured by
///     subtraction: the provider's classic path copied (<see cref="ClassicJsonProbeProvider" />), one thing taken
///     out per row, cumulatively.
/// </summary>
/// <remarks>
///     Not part of <c>all</c> and not in the allocation ratchet: it explains a number (#284).
///     <c>dotnet run -c Release -- classic-json-probe</c>.
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
public partial class ClassicJsonProbeBenchmarks
{
    private static readonly ClassicJsonProbeVariant[] Variants =
    [
        ClassicJsonProbeVariant.Same,
        ClassicJsonProbeVariant.ReusedWriter,
        ClassicJsonProbeVariant.ReusedWriter | ClassicJsonProbeVariant.BytesOut,
        ClassicJsonProbeVariant.ReusedWriter | ClassicJsonProbeVariant.BytesOut | ClassicJsonProbeVariant.StackTimestamp,
        ClassicJsonProbeVariant.EmptyWrite,
    ];

    private readonly int _orderId = 42;
    private readonly string _customer = "jane";
    private readonly decimal _amount = 19.99m;

    private readonly List<IDisposable> _owned = [];
    private readonly ILogger[] _probes = new ILogger[Variants.Length];
    private ILogger _real = null!;
    private ILogger _emptyNoProperties = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var realOutput = new LastLineStream();
        _real = Logger(new PragmaticJsonProvider("Json", Configuration(), realOutput));
        RealProvider();
        var realLine = Timestamp().Replace(realOutput.LastLine, "\"@timestamp\":\"T\"");

        for (var i = 0; i < Variants.Length; i++)
        {
            var output = new LastLineStream();
            var provider = new ClassicJsonProbeProvider(Configuration(), output, Variants[i]);
            _probes[i] = Logger(provider);
            Call(i);
            if (provider.Lines != 1)
                throw new InvalidOperationException($"The probe row {Variants[i]} did not reach the classic path.");
            if (Variants[i] == ClassicJsonProbeVariant.EmptyWrite)
                continue;

            var line = Timestamp().Replace(output.LastLine, "\"@timestamp\":\"T\"");
            if (line != realLine)
                throw new InvalidOperationException(
                    $"The probe row {Variants[i]} wrote{Environment.NewLine}{line}{Environment.NewLine}where the provider wrote{Environment.NewLine}{realLine}");
        }

        var noProperties = Configuration();
        noProperties.IncludeStructuredProperties = false;
        var empty = new ClassicJsonProbeProvider(noProperties, new LastLineStream(), ClassicJsonProbeVariant.EmptyWrite);
        _emptyNoProperties = Logger(empty);
        EmptyWrite_NoProperties();
        if (empty.Lines != 1)
            throw new InvalidOperationException("The probe row EmptyWrite_NoProperties did not reach the classic path.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    [Benchmark]
    public void RealProvider() => MicrosoftJsonLog.OrderPlaced(_real, _orderId, _customer, _amount);

    [Benchmark(Baseline = true)]
    public void Same() => Call(0);

    [Benchmark]
    public void ReusedWriter() => Call(1);

    [Benchmark]
    public void ReusedWriter_BytesOut() => Call(2);

    [Benchmark]
    public void ReusedWriter_BytesOut_StackTimestamp() => Call(3);

    [Benchmark]
    public void EmptyWrite() => Call(4);

    /// <summary>Nothing written, and the call's properties not read out of the state.</summary>
    [Benchmark]
    public void EmptyWrite_NoProperties() => MicrosoftJsonLog.OrderPlaced(_emptyNoProperties, _orderId, _customer, _amount);

    private void Call(int row) => MicrosoftJsonLog.OrderPlaced(_probes[row], _orderId, _customer, _amount);

    // The configuration JsonSinkBenchmarks gives the real provider.
    private static PragmaticProviderConfiguration Configuration()
    {
        var configuration = Providers.PragmaticJsonConfiguration.ForJson();
        configuration.MinimumLevel = LogLevel.Information;
        configuration.IncludeContextEnrichment = false;
        configuration.CustomProperties["AutoFlush"] = true;
        return configuration;
    }

    private ILogger Logger(PragmaticLoggerProviderBase provider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder => builder.AddProvider(_ => provider));
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private T Owned<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    [System.Text.RegularExpressions.GeneratedRegex("\"@timestamp\":\"[^\"]*\"")]
    private static partial System.Text.RegularExpressions.Regex Timestamp();
}
