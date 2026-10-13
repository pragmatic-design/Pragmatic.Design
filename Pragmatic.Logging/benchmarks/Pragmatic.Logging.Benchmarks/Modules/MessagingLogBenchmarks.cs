using System.Globalization;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Benchmarks.Json;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     Pragmatic.Messaging's per-message logging on Microsoft's generator and as generated call sites (#114):
///     the bus's own message shapes, declared once per generator, called through the same logger.
/// </summary>
/// <remarks>
///     <para>
///         The bus logs every publish at Debug (<c>LogPublishing</c>). In production that level is off, and
///         what the call costs is the level check (<c>MessagingDisabled</c>); with Debug on, to diagnose a
///         flow, it is a JSON line per message (<c>MessagingDebugJson</c>). A failed handler logs at Error,
///         which is on in production, with the exception (<c>MessagingErrorJson</c>).
///     </para>
///     <para>
///         <see cref="Setup" /> checks the work is the same before timing anything: both rows of a JSON
///         category write the same message and event id, and neither disabled row writes anything.
///     </para>
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public partial class MessagingLogBenchmarks
{
    private const string MessageType = "OrderPlaced";
    private const string Topic = "sales.orders";
    private const string Transport = "rabbitmq";
    private const string MessageId = "0192f3a4-5b6c-7d8e-9f01-23456789abcd";
    private const string HandlerName = "ReserveStockHandler";

    private readonly LastLineStream _microsoftInformationStream = new();
    private readonly LastLineStream _callSiteInformationStream = new();
    private readonly LastLineStream _microsoftDebugStream = new();
    private readonly LastLineStream _callSiteDebugStream = new();
    private readonly List<IDisposable> _owned = [];

    private ILogger _informationLogger = null!;
    private MessagingMicrosoftLog _microsoft = null!;
    private MessagingCallSiteLog _callSite = null!;
    private MessagingMicrosoftLog _microsoftDebug = null!;
    private MessagingCallSiteLog _callSiteDebug = null!;
    private Exception _failure = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _microsoft = new MessagingMicrosoftLog(Logger(_microsoftInformationStream, LogLevel.Information));
        _callSite = new MessagingCallSiteLog(Logger(_callSiteInformationStream, LogLevel.Information));
        _informationLogger = Logger(new LastLineStream(), LogLevel.Information);
        _microsoftDebug = new MessagingMicrosoftLog(Logger(_microsoftDebugStream, LogLevel.Debug));
        _callSiteDebug = new MessagingCallSiteLog(Logger(_callSiteDebugStream, LogLevel.Debug));
        _failure = Thrown();

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    // ── MessagingDisabled: LogPublishing at Debug, the logger at Information ──

    [Benchmark(Baseline = true), BenchmarkCategory("MessagingDisabled")]
    public void Messaging_Disabled_LoggerMessage() => _microsoft.LogPublishing(MessageType, Topic, Transport, MessageId);

    [Benchmark, BenchmarkCategory("MessagingDisabled")]
    public void Messaging_Disabled_CallSite() => _callSite.LogPublishing(MessageType, Topic, Transport, MessageId);

    /// <summary>
    ///     Subtraction (#175): the level check alone, on the same logger, written by hand. What either generated
    ///     method costs below its level beyond it is what the generator adds.
    /// </summary>
    [Benchmark, BenchmarkCategory("MessagingDisabled")]
    public bool Messaging_Disabled_LevelCheckOnly() => _informationLogger.IsEnabled(LogLevel.Debug);

    // ── MessagingDisabledReversed: the same pair, the call site declared and run first (#175) ──
    // The two methods now compile to the same instructions up to the level check; a gap that follows the
    // order the rows run in, rather than the row, is the machine's.

    [Benchmark, BenchmarkCategory("MessagingDisabledReversed")]
    public void Messaging_DisabledReversed_CallSite() => _callSite.LogPublishing(MessageType, Topic, Transport, MessageId);

    [Benchmark(Baseline = true), BenchmarkCategory("MessagingDisabledReversed")]
    public void Messaging_DisabledReversed_LoggerMessage() => _microsoft.LogPublishing(MessageType, Topic, Transport, MessageId);

    // ── MessagingDebugJson: LogPublishing as a JSON line, the logger at Debug ──

    [Benchmark(Baseline = true), BenchmarkCategory("MessagingDebugJson")]
    public void Messaging_DebugJson_LoggerMessage() => _microsoftDebug.LogPublishing(MessageType, Topic, Transport, MessageId);

    [Benchmark, BenchmarkCategory("MessagingDebugJson")]
    public void Messaging_DebugJson_CallSite() => _callSiteDebug.LogPublishing(MessageType, Topic, Transport, MessageId);

    // ── MessagingErrorJson: LogHandlerFailed with its exception as a JSON line ──

    [Benchmark(Baseline = true), BenchmarkCategory("MessagingErrorJson")]
    public void Messaging_ErrorJson_LoggerMessage() => _microsoft.LogHandlerFailed(MessageType, HandlerName, _failure);

    [Benchmark, BenchmarkCategory("MessagingErrorJson")]
    public void Messaging_ErrorJson_CallSite() => _callSite.LogHandlerFailed(MessageType, HandlerName, _failure);

    // ── Setup ──

    private ILogger Logger(Stream output, LogLevel minimumLevel)
    {
        var configuration = Providers.PragmaticJsonConfiguration.ForJson();
        configuration.MinimumLevel = minimumLevel;
        configuration.IncludeContextEnrichment = false;
        configuration.CustomProperties["AutoFlush"] = true;

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(minimumLevel));
        services.AddPragmaticLoggingBuilder(builder =>
            builder.AddProvider(_ => new PragmaticJsonProvider("Json", configuration, output)));
        var provider = services.BuildServiceProvider();
        _owned.Add(provider);
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("Pragmatic.Messaging.TransportAwareMessageBus");
    }

    // A failure as a handler raises one: thrown, so that it carries a stack trace for the sink to write.
    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("Stock reservation failed");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    /// <summary>
    ///     Throws unless both rows of each JSON category wrote a line with the same message and event id, and
    ///     neither disabled row wrote one.
    /// </summary>
    internal void VerifySameWork()
    {
        var failures = new List<string>();

        Messaging_DebugJson_LoggerMessage();
        Messaging_DebugJson_CallSite();
        CompareLines("DebugJson", _microsoftDebugStream.LastLine, _callSiteDebugStream.LastLine,
            "Publishing OrderPlaced to topic sales.orders via rabbitmq (id: " + MessageId + ")", failures);

        Messaging_ErrorJson_LoggerMessage();
        Messaging_ErrorJson_CallSite();
        var microsoft = _microsoftInformationStream.LastLine;
        var callSite = _callSiteInformationStream.LastLine;
        CompareLines("ErrorJson", microsoft, callSite, "Handler ReserveStockHandler failed for message OrderPlaced", failures);
        foreach (var (row, line) in new[] { ("[LoggerMessage]", microsoft), ("call site", callSite) })
        {
            if (!line.Contains("Stock reservation failed", StringComparison.Ordinal))
                failures.Add($"  ErrorJson {row,-16} wrote no exception: {line}");
        }

        Messaging_Disabled_LoggerMessage();
        Messaging_Disabled_CallSite();
        if (_microsoftInformationStream.LastLine != microsoft)
            failures.Add($"  [LoggerMessage] LogPublishing wrote at Debug: {_microsoftInformationStream.LastLine}");
        if (_callSiteInformationStream.LastLine != callSite)
            failures.Add($"  call site       LogPublishing wrote at Debug: {_callSiteInformationStream.LastLine}");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "The Messaging rows do not do the same work:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static void CompareLines(string category, string microsoft, string callSite, string message, List<string> failures)
    {
        var microsoftId = EventIdOf(microsoft);
        var callSiteId = EventIdOf(callSite);
        if (microsoftId.Length == 0 || microsoftId != callSiteId)
            failures.Add($"  {category}: event ids differ ('{microsoftId}' against '{callSiteId}')");

        foreach (var (row, line) in new[] { ("[LoggerMessage]", microsoft), ("call site", callSite) })
        {
            if (!line.Contains(message, StringComparison.Ordinal))
                failures.Add($"  {category} {row,-16} {line}");
        }
    }

    private static string EventIdOf(string line) => EventIdPattern().Match(line).Groups[1].Value;

    [GeneratedRegex("""[eE]ventId"\s*:\s*(\d+)""")]
    private static partial Regex EventIdPattern();
}
