using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using ZLogger;
using ZLogger.Formatters;

namespace Pragmatic.Logging.Benchmarks.Json.Probe;

/// <summary>
///     Where the time of a generated call site's JSON line goes, measured by subtraction: the JSON provider's
///     path copied (<see cref="ProbeJsonProvider" />), one thing taken out or done differently per row, against
///     ZLogger in the same run.
/// </summary>
/// <remarks>
///     Not part of <c>all</c> and not in the allocation ratchet: it explains a number, it is not one to keep.
///     <c>dotnet run -c Release -- probe</c>.
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
public partial class ProbeBenchmarks
{
    // Indexed by row, so a row pays an array read and not a lookup the real call site does not make.
    private static readonly ProbeVariant[] Variants =
    [
        ProbeVariant.Same,
        ProbeVariant.ParsedEachLine,
        ProbeVariant.BuiltInTimestamp,
        ProbeVariant.NoTemplate,
        ProbeVariant.NoExtraFields,
        ProbeVariant.EncodedEventFields,
        ProbeVariant.EncodedConstants,
        ProbeVariant.RawMessage,
        ProbeVariant.NoLock,
        ProbeVariant.NoStream,
        ProbeVariant.AutoFlushOnce,
        ProbeVariant.EmptyWrite,
        ProbeVariant.ConstantBlocks,
        ProbeVariant.ConstantBlocks | ProbeVariant.EventBlockOnly,
        ProbeVariant.ConstantBlocks | ProbeVariant.BuiltInTimestamp,
        ProbeVariant.ConstantBlocks | ProbeVariant.NoLock,
        ProbeVariant.ConstantBlocks | ProbeVariant.NoStream,
    ];

    // The rows that claim the line Same writes: the setup holds them to it, timestamp aside.
    private static readonly ProbeVariant[] SameBytes =
    [
        ProbeVariant.ParsedEachLine, ProbeVariant.EncodedEventFields, ProbeVariant.EncodedConstants,
        ProbeVariant.NoLock, ProbeVariant.AutoFlushOnce, ProbeVariant.ConstantBlocks,
        ProbeVariant.ConstantBlocks | ProbeVariant.EventBlockOnly,
    ];

    private readonly int _orderId = 42;
    private readonly string _customer = "jane";
    private readonly decimal _amount = 19.99m;

    private readonly List<IDisposable> _owned = [];
    private readonly ILogger[] _probes = new ILogger[Variants.Length];
    private ILogger _real = null!;
    private ILogger _zloggerSameFields = null!;
    private ILogger _zloggerEmpty = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var lines = new string[Variants.Length];
        for (var i = 0; i < Variants.Length; i++)
        {
            var output = new LastLineStream();
            var provider = new ProbeJsonProvider(Configuration(), output, Variants[i]);
            _probes[i] = Logger(provider);
            Call(i);
            if (provider.Lines != 1)
                throw new InvalidOperationException($"The probe row {Variants[i]} did not reach the UTF-8 path.");
            lines[i] = Timestamp().Replace(output.LastLine, "\"@timestamp\":\"T\"");

            var built = provider.BlocksBuilt;
            for (var call = 0; call < 10; call++)
                Call(i);
            if (provider.BlocksBuilt != built)
                throw new InvalidOperationException(
                    $"The probe row {Variants[i]} encoded {provider.BlocksBuilt - built} blocks in 10 calls after the first.");
        }

        foreach (var variant in SameBytes)
        {
            var line = lines[Array.IndexOf(Variants, variant)];
            if (line != lines[0])
                throw new InvalidOperationException($"The probe row {variant} wrote{Environment.NewLine}{line}{Environment.NewLine}where Same wrote{Environment.NewLine}{lines[0]}");
        }

        var realOutput = new LastLineStream();
        _real = Logger(new PragmaticJsonProvider("Json", Configuration(), realOutput));
        RealProvider();
        var realLine = Timestamp().Replace(realOutput.LastLine, "\"@timestamp\":\"T\"");
        if (realLine != lines[0])
            throw new InvalidOperationException($"The provider wrote{Environment.NewLine}{realLine}{Environment.NewLine}where Same wrote{Environment.NewLine}{lines[0]}");
        _zloggerSameFields = ZLoggerWith(
            options => new ZLoggerJsonStreamProcessor(options.CreateFormatter(), new LastLineStream()),
            JsonSinkBenchmarks.PragmaticFields);
        _zloggerEmpty = ZLoggerWith(_ => new ZLoggerEmptyProcessor());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    [Benchmark]
    public void RealProvider() => PragmaticJsonLog.OrderPlaced(_real, _orderId, _customer, _amount);

    [Benchmark(Baseline = true)]
    public void Same() => Call(0);

    [Benchmark]
    public void ParsedEachLine() => Call(1);

    [Benchmark]
    public void BuiltInTimestamp() => Call(2);

    [Benchmark]
    public void NoTemplate() => Call(3);

    [Benchmark]
    public void NoExtraFields() => Call(4);

    [Benchmark]
    public void EncodedEventFields() => Call(5);

    [Benchmark]
    public void EncodedConstants() => Call(6);

    [Benchmark]
    public void RawMessage() => Call(7);

    [Benchmark]
    public void NoLock() => Call(8);

    [Benchmark]
    public void NoStream() => Call(9);

    [Benchmark]
    public void AutoFlushOnce() => Call(10);

    [Benchmark]
    public void EmptyWrite() => Call(11);

    [Benchmark]
    public void ConstantBlocks() => Call(12);

    [Benchmark]
    public void EventBlockOnly() => Call(13);

    [Benchmark]
    public void Blocks_BuiltInTimestamp() => Call(14);

    [Benchmark]
    public void Blocks_NoLock() => Call(15);

    [Benchmark]
    public void Blocks_NoStream() => Call(16);

    [Benchmark]
    public void ZLogger_SameFields_Stream() => ZLoggerJsonLog.OrderPlaced(_zloggerSameFields, _orderId, _customer, _amount);

    [Benchmark]
    public void ZLogger_Empty() => ZLoggerJsonLog.OrderPlaced(_zloggerEmpty, _orderId, _customer, _amount);

    [Benchmark]
    public bool IsEnabled_Pragmatic() => _probes[0].IsEnabled(LogLevel.Information);

    [Benchmark]
    public bool IsEnabled_ZLogger() => _zloggerEmpty.IsEnabled(LogLevel.Information);

    private void Call(int row) => PragmaticJsonLog.OrderPlaced(_probes[row], _orderId, _customer, _amount);

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

    private ILogger ZLoggerWith(Func<ZLoggerOptions, IAsyncLogProcessor> processor, Action<SystemTextJsonZLoggerFormatter>? formatter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddZLoggerLogProcessor(options =>
            {
                options.UseJsonFormatter(formatter);
                return processor(options);
            });
        });
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
