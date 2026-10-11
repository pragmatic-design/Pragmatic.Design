using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Benchmarks.Json;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     Pragmatic.Actions' invoker logging, before and after its move to generated call sites (#114): the
///     module's own message shapes, declared once per generator, called through the same logger.
/// </summary>
/// <remarks>
///     <para>
///         Two cases, chosen from what the invoker does on every operation. Most of its messages are Debug or
///         Trace, so in production they are disabled and what they cost is the level check
///         (<c>ActionsDisabled</c>). <c>LogSuccess</c> is Information, written once per successful action;
///         here as a JSON line by Pragmatic's provider, the path where a call site writes its state as UTF-8
///         without rendering a string first (<c>ActionsJson</c>).
///     </para>
///     <para>
///         <see cref="Setup" /> checks the work is the same before timing anything: both <c>LogSuccess</c> rows
///         write the same message and event id, and neither <c>LogStart</c> row writes anything.
///     </para>
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ActionsLogBenchmarks
{
    private const string Kind = "Action";
    private const string ActionName = "PlaceOrder";
    private const long ElapsedMs = 12;

    private readonly LastLineStream _microsoftStream = new();
    private readonly LastLineStream _callSiteStream = new();
    private readonly List<IDisposable> _owned = [];

    private ActionsMicrosoftLog _microsoft = null!;
    private ActionsCallSiteLog _callSite = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _microsoft = new ActionsMicrosoftLog(Logger(_microsoftStream));
        _callSite = new ActionsCallSiteLog(Logger(_callSiteStream));

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    // ── ActionsDisabled: LogStart at Debug, the logger at Information ──

    [Benchmark(Baseline = true), BenchmarkCategory("ActionsDisabled")]
    public void Actions_Disabled_LoggerMessage() => _microsoft.LogStart(Kind, ActionName);

    [Benchmark, BenchmarkCategory("ActionsDisabled")]
    public void Actions_Disabled_CallSite() => _callSite.LogStart(Kind, ActionName);

    // ── ActionsJson: LogSuccess as a JSON line ──

    [Benchmark(Baseline = true), BenchmarkCategory("ActionsJson")]
    public void Actions_Json_LoggerMessage() => _microsoft.LogSuccess(Kind, ActionName, ElapsedMs);

    [Benchmark, BenchmarkCategory("ActionsJson")]
    public void Actions_Json_CallSite() => _callSite.LogSuccess(Kind, ActionName, ElapsedMs);

    // ── Setup ──

    private ILogger Logger(Stream output)
    {
        var configuration = Providers.PragmaticJsonConfiguration.ForJson();
        configuration.MinimumLevel = LogLevel.Information;
        configuration.IncludeContextEnrichment = false;
        configuration.CustomProperties["AutoFlush"] = true;

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information));
        services.AddPragmaticLoggingBuilder(builder =>
            builder.AddProvider(_ => new PragmaticJsonProvider("Json", configuration, output)));
        var provider = services.BuildServiceProvider();
        _owned.Add(provider);
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("Pragmatic.Actions.Invoker");
    }

    /// <summary>
    ///     Throws unless both <c>LogSuccess</c> rows wrote a line with the same message and event id, and
    ///     neither <c>LogStart</c> row wrote one.
    /// </summary>
    internal void VerifySameWork()
    {
        var failures = new List<string>();

        Actions_Json_LoggerMessage();
        Actions_Json_CallSite();
        var microsoft = _microsoftStream.LastLine;
        var callSite = _callSiteStream.LastLine;
        foreach (var (row, line) in new[] { ("[LoggerMessage]", microsoft), ("call site", callSite) })
        {
            if (!line.Contains("Action PlaceOrder succeeded in 12ms", StringComparison.Ordinal)
                || !line.Contains("1535103852", StringComparison.Ordinal))
                failures.Add($"  {row,-16} {line}");
        }

        Actions_Disabled_LoggerMessage();
        Actions_Disabled_CallSite();
        if (_microsoftStream.LastLine != microsoft)
            failures.Add($"  [LoggerMessage] LogStart wrote at Debug: {_microsoftStream.LastLine}");
        if (_callSiteStream.LastLine != callSite)
            failures.Add($"  call site       LogStart wrote at Debug: {_callSiteStream.LastLine}");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "The Actions rows do not do the same work:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
