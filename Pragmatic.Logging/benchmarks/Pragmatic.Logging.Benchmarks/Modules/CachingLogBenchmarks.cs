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
///     Pragmatic.Caching's per-read logging on Microsoft's generator and as generated call sites (#114): the
///     cache stack's own message shapes, declared once per generator, called through the same logger.
/// </summary>
/// <remarks>
///     <para>
///         The stack logs every read through the cache at Debug (<c>LogGetOrSet</c>). In production that level
///         is off, and what the call costs is the level check (<c>CachingDisabled</c>); with Debug on, to
///         diagnose a key, it is a JSON line per read (<c>CachingDebugJson</c>). A tag a multi-tag
///         invalidation could not clear logs at Warning, which is on in production, with the exception
///         (<c>CachingWarningJson</c>).
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
public partial class CachingLogBenchmarks
{
    private const string Key = "t:acme:catalog:products:page:3:size:50";
    private const string Tag = "catalog:products";

    private readonly LastLineStream _microsoftInformationStream = new();
    private readonly LastLineStream _callSiteInformationStream = new();
    private readonly LastLineStream _microsoftDebugStream = new();
    private readonly LastLineStream _callSiteDebugStream = new();
    private readonly List<IDisposable> _owned = [];

    private CachingMicrosoftLog _microsoft = null!;
    private CachingCallSiteLog _callSite = null!;
    private CachingMicrosoftLog _microsoftDebug = null!;
    private CachingCallSiteLog _callSiteDebug = null!;
    private Exception _failure = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _microsoft = new CachingMicrosoftLog(Logger(_microsoftInformationStream, LogLevel.Information));
        _callSite = new CachingCallSiteLog(Logger(_callSiteInformationStream, LogLevel.Information));
        _microsoftDebug = new CachingMicrosoftLog(Logger(_microsoftDebugStream, LogLevel.Debug));
        _callSiteDebug = new CachingCallSiteLog(Logger(_callSiteDebugStream, LogLevel.Debug));
        _failure = Thrown();

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    // ── CachingDisabled: LogGetOrSet at Debug, the logger at Information ──

    [Benchmark(Baseline = true), BenchmarkCategory("CachingDisabled")]
    public void Caching_Disabled_LoggerMessage() => _microsoft.LogGetOrSet(Key);

    [Benchmark, BenchmarkCategory("CachingDisabled")]
    public void Caching_Disabled_CallSite() => _callSite.LogGetOrSet(Key);

    // ── CachingDebugJson: LogGetOrSet as a JSON line, the logger at Debug ──

    [Benchmark(Baseline = true), BenchmarkCategory("CachingDebugJson")]
    public void Caching_DebugJson_LoggerMessage() => _microsoftDebug.LogGetOrSet(Key);

    [Benchmark, BenchmarkCategory("CachingDebugJson")]
    public void Caching_DebugJson_CallSite() => _callSiteDebug.LogGetOrSet(Key);

    // ── CachingWarningJson: LogTagInvalidationFailed with its exception as a JSON line ──

    [Benchmark(Baseline = true), BenchmarkCategory("CachingWarningJson")]
    public void Caching_WarningJson_LoggerMessage() => _microsoft.LogTagInvalidationFailed(Tag, _failure);

    [Benchmark, BenchmarkCategory("CachingWarningJson")]
    public void Caching_WarningJson_CallSite() => _callSite.LogTagInvalidationFailed(Tag, _failure);

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
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("Pragmatic.Caching.HybridCacheStack");
    }

    // A failure as a distributed tier raises one: thrown, so that it carries a stack trace for the sink to write.
    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("The distributed cache did not answer");
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

        Caching_DebugJson_LoggerMessage();
        Caching_DebugJson_CallSite();
        CompareLines("DebugJson", _microsoftDebugStream.LastLine, _callSiteDebugStream.LastLine,
            "Cache get-or-set key='t:acme:catalog:products:page:3:size:50'", failures);

        Caching_WarningJson_LoggerMessage();
        Caching_WarningJson_CallSite();
        var microsoft = _microsoftInformationStream.LastLine;
        var callSite = _callSiteInformationStream.LastLine;
        CompareLines("WarningJson", microsoft, callSite, "Cache tag invalidation failed for tag='catalog:products'", failures);
        foreach (var (row, line) in new[] { ("[LoggerMessage]", microsoft), ("call site", callSite) })
        {
            if (!line.Contains("The distributed cache did not answer", StringComparison.Ordinal))
                failures.Add($"  WarningJson {row,-16} wrote no exception: {line}");
        }

        Caching_Disabled_LoggerMessage();
        Caching_Disabled_CallSite();
        if (_microsoftInformationStream.LastLine != microsoft)
            failures.Add($"  [LoggerMessage] LogGetOrSet wrote at Debug: {_microsoftInformationStream.LastLine}");
        if (_callSiteInformationStream.LastLine != callSite)
            failures.Add($"  call site       LogGetOrSet wrote at Debug: {_callSiteInformationStream.LastLine}");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "The Caching rows do not do the same work:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
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
