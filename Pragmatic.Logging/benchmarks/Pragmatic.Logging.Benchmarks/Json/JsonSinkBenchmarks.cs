using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Serilog.Extensions.Logging;
using ZLogger;
using ZLogger.Formatters;

namespace Pragmatic.Logging.Benchmarks.Json;

/// <summary>
///     One call — an int, a string and a decimal — written as a JSON line by each library's own JSON
///     writer into a reused buffer.
/// </summary>
/// <remarks>
///     <para>
///         Each library is called through <c>Microsoft.Extensions.Logging</c> with its best call site:
///         Pragmatic's generated one, Microsoft's <c>[LoggerMessage]</c> (through Pragmatic, Serilog and
///         NLog), ZLogger's <c>[ZLoggerMessage]</c>. Every sink formats on the logging thread and keeps only
///         the line being written, so a row measures building the line and not storing it.
///     </para>
///     <para>
///         <see cref="Setup" /> checks the work is the same before timing anything: every line must carry the
///         rendered message and the three values. Context enrichment is off for Pragmatic, as nothing like it
///         is configured for the others.
///     </para>
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class JsonSinkBenchmarks
{
    private const string Message = "Order 42 placed by jane for 19.99";
    private const string Email = "alice@example.com";

    private readonly int _orderId = 42;
    private readonly string _customer = "jane";
    private readonly decimal _amount = 19.99m;

    private readonly LastLineStream _pragmaticStream = new();
    private readonly LastLineStream _pragmaticClassicStream = new();
    private readonly SerilogJsonSink _serilogSink = new();
    private readonly NLogJsonTarget _nlogTarget = new();
    private ZLoggerJsonProcessor _zloggerProcessor = null!;

    private ILogger _pragmatic = null!;
    private ILogger _pragmaticClassic = null!;
    private ILogger _serilog = null!;
    private ILogger _nlog = null!;
    private ILogger _zlogger = null!;
    private readonly LastLineStream _zloggerStreamOutput = new();
    private ILogger _zloggerStream = null!;
    private readonly LastLineStream _zloggerSameFieldsOutput = new();
    private ILogger _zloggerSameFields = null!;

    private readonly List<IDisposable> _owned = [];

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _pragmatic = Pragmatic(_pragmaticStream);
        _pragmaticClassic = Pragmatic(_pragmaticClassicStream);
        _serilog = Serilog();
        _nlog = NLog();
        _zlogger = ZLogger(options => _zloggerProcessor = new ZLoggerJsonProcessor(options.CreateFormatter()));
        _zloggerStream = ZLogger(options => new ZLoggerJsonStreamProcessor(options.CreateFormatter(), _zloggerStreamOutput));
        _zloggerSameFields = ZLogger(
            options => new ZLoggerJsonStreamProcessor(options.CreateFormatter(), _zloggerSameFieldsOutput),
            PragmaticFields);

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        global::NLog.LogManager.Shutdown();
    }

    // ── Json: the same call, a JSON line ──

    [Benchmark(Baseline = true), BenchmarkCategory("Json")]
    public void Pragmatic_CallSite() => PragmaticJsonLog.OrderPlaced(_pragmatic, _orderId, _customer, _amount);

    [Benchmark, BenchmarkCategory("Json")]
    public void Pragmatic_LoggerMessage() => MicrosoftJsonLog.OrderPlaced(_pragmaticClassic, _orderId, _customer, _amount);

    [Benchmark, BenchmarkCategory("Json")]
    public void Serilog_LoggerMessage() => MicrosoftJsonLog.OrderPlaced(_serilog, _orderId, _customer, _amount);

    [Benchmark, BenchmarkCategory("Json")]
    public void NLog_LoggerMessage() => MicrosoftJsonLog.OrderPlaced(_nlog, _orderId, _customer, _amount);

    [Benchmark, BenchmarkCategory("Json")]
    public void ZLogger_ZLoggerMessage() => ZLoggerJsonLog.OrderPlaced(_zlogger, _orderId, _customer, _amount);

    /// <summary>ZLogger with the sink Pragmatic's provider has: a stream, under a lock, flushed per line.</summary>
    [Benchmark, BenchmarkCategory("Json")]
    public void ZLogger_ZLoggerMessage_Stream() => ZLoggerJsonLog.OrderPlaced(_zloggerStream, _orderId, _customer, _amount);

    /// <summary>
    ///     ZLogger writing the fields Pragmatic's line has, under the same names, through the same sink: event id
    ///     and name, a UTC timestamp, the arguments under <c>@properties</c>. All but the template, which its
    ///     formatter has no field for.
    /// </summary>
    [Benchmark, BenchmarkCategory("Json")]
    public void ZLogger_ZLoggerMessage_SameFields_Stream() =>
        ZLoggerJsonLog.OrderPlaced(_zloggerSameFields, _orderId, _customer, _amount);

    // ── JsonPersonalData: an argument declared personal data, masked by the call site ──

    /// <summary>Pragmatic only: no other library masks an argument because its parameter says so.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("JsonPersonalData")]
    public void Pragmatic_CallSite_PersonalData() => PragmaticJsonLog.ReceiptSent(_pragmatic, _orderId, Email);

    // ── Setup ──

    private ILogger Pragmatic(Stream output)
    {
        var configuration = Providers.PragmaticJsonConfiguration.ForJson();
        configuration.MinimumLevel = LogLevel.Information;
        configuration.IncludeContextEnrichment = false;
        // A line per call, as every other sink here writes one.
        configuration.CustomProperties["AutoFlush"] = true;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder =>
            builder.AddProvider(_ => new PragmaticJsonProvider("Json", configuration, output)));
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private ILogger Serilog()
    {
        var serilog = new global::Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(_serilogSink)
            .CreateLogger();
        return Owned(new SerilogLoggerFactory(serilog, dispose: true)).CreateLogger("Benchmark");
    }

    private ILogger NLog()
    {
        var config = new global::NLog.Config.LoggingConfiguration();
        config.AddRule(global::NLog.LogLevel.Info, global::NLog.LogLevel.Fatal, _nlogTarget);
        global::NLog.LogManager.Configuration = config;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddNLog();
        });
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private ILogger ZLogger(Func<ZLoggerOptions, IAsyncLogProcessor> processor, Action<SystemTextJsonZLoggerFormatter>? formatter = null)
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

    internal static void PragmaticFields(SystemTextJsonZLoggerFormatter formatter)
    {
        formatter.IncludeProperties = IncludeProperties.Default | IncludeProperties.EventIdValue | IncludeProperties.EventIdName;
        formatter.UseUtcTimestamp = true;
        formatter.PropertyKeyValuesObjectName = JsonEncodedText.Encode("@properties");
        formatter.JsonPropertyNames = JsonPropertyNames.Default with
        {
            Timestamp = JsonEncodedText.Encode("@timestamp"),
            LogLevel = JsonEncodedText.Encode("@level"),
            Category = JsonEncodedText.Encode("@logger"),
            Message = JsonEncodedText.Encode("@message"),
            EventId = JsonEncodedText.Encode("@eventId"),
            EventIdName = JsonEncodedText.Encode("@eventName"),
            LogLevelInformation = JsonEncodedText.Encode("INFO"),
        };
    }

    private T Owned<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    // ── The equivalence check ──

    /// <summary>
    ///     Writes one line through each library and throws unless every line is JSON carrying the rendered
    ///     message and the three values, and the personal-data line carries the mask instead of the address.
    /// </summary>
    internal void VerifySameWork()
    {
        var lines = new (string Library, Action Call, Func<string> Line)[]
        {
            ("Pragmatic call site", Pragmatic_CallSite, () => _pragmaticStream.LastLine),
            ("Pragmatic [LoggerMessage]", Pragmatic_LoggerMessage, () => _pragmaticClassicStream.LastLine),
            ("Serilog", Serilog_LoggerMessage, () => _serilogSink.LastLine),
            ("NLog", NLog_LoggerMessage, () => _nlogTarget.LastLine),
            ("ZLogger", ZLogger_ZLoggerMessage, () => _zloggerProcessor.LastLine),
            ("ZLogger, stream", ZLogger_ZLoggerMessage_Stream, () => _zloggerStreamOutput.LastLine),
            ("ZLogger, same fields", ZLogger_ZLoggerMessage_SameFields_Stream, () => _zloggerSameFieldsOutput.LastLine),
        };

        var failures = new List<string>();
        foreach (var (library, call, line) in lines)
        {
            call();
            var written = line();
            var values = Values(written);
            // Serilog quotes a string in the rendered message by default ("jane"); the value is the same.
            var hasMessage = values.Any(v => v.Replace("\"", "", StringComparison.Ordinal) == Message);
            if (!hasMessage || !values.Contains("42") || !values.Contains("jane") || !values.Contains("19.99"))
                failures.Add($"  {library,-26} {written}");
        }

        // The row that claims Pragmatic's fields must write them, or it compares against a shorter line.
        var sameFields = _zloggerSameFieldsOutput.LastLine;
        foreach (var field in new[] { "\"@timestamp\":", "\"@level\":\"INFO\"", "\"@logger\":", "\"@message\":", "\"@eventId\":2001", "\"@eventName\":", "\"@properties\":{" })
        {
            if (!sameFields.Contains(field, StringComparison.Ordinal))
                failures.Add($"  {"ZLogger, same fields",-26} has no {field}: {sameFields}");
        }

        Pragmatic_CallSite_PersonalData();
        var masked = _pragmaticStream.LastLine;
        if (masked.Contains(Email, StringComparison.Ordinal) || !masked.Contains("[redacted]", StringComparison.Ordinal))
            failures.Add($"  {"Pragmatic personal data",-26} {masked}");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "The JSON lines do not carry the same call, so the rows would not measure the same work:"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>Every string and number in a JSON line, as text. A line that is not JSON throws.</summary>
    private static HashSet<string> Values(string line)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(line);
        Collect(document.RootElement, values);
        return values;
    }

    private static void Collect(JsonElement element, HashSet<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, values);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, values);
                break;
            case JsonValueKind.String:
                values.Add(element.GetString()!);
                break;
            case JsonValueKind.Number:
                values.Add(element.GetRawText());
                break;
        }
    }
}
