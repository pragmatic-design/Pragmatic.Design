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
///     Pragmatic.Persistence.EFCore's per-query logging on Microsoft's generator and as generated call sites
///     (#114): the query executor's own message shapes, declared once per generator, called through the same
///     logger.
/// </summary>
/// <remarks>
///     <para>
///         The executor logs every paged read at Debug (<c>LogQueryCompleted</c>). In production that level is
///         off, and what the call costs is the level check (<c>EfCoreDisabled</c>); with Debug on, to diagnose
///         a page, it is a JSON line per read (<c>EfCoreDebugJson</c>). A read that fails logs at Error, which
///         is on in production, with the exception (<c>EfCoreErrorJson</c>). The unit of work's methods are the
///         same shapes with fewer arguments, and are not measured separately.
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
public partial class EfCoreLogBenchmarks
{
    private const string QueryType = "OrdersByCustomerQuery";
    private const string ErrorType = "Database";
    private const int TotalCount = 1342;
    private const int Page = 3;
    private const int PageSize = 50;

    private readonly LastLineStream _microsoftInformationStream = new();
    private readonly LastLineStream _callSiteInformationStream = new();
    private readonly LastLineStream _microsoftDebugStream = new();
    private readonly LastLineStream _callSiteDebugStream = new();
    private readonly List<IDisposable> _owned = [];

    private EfCoreMicrosoftLog _microsoft = null!;
    private EfCoreCallSiteLog _callSite = null!;
    private EfCoreMicrosoftLog _microsoftDebug = null!;
    private EfCoreCallSiteLog _callSiteDebug = null!;
    private Exception _failure = null!;

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _microsoft = new EfCoreMicrosoftLog(Logger(_microsoftInformationStream, LogLevel.Information));
        _callSite = new EfCoreCallSiteLog(Logger(_callSiteInformationStream, LogLevel.Information));
        _microsoftDebug = new EfCoreMicrosoftLog(Logger(_microsoftDebugStream, LogLevel.Debug));
        _callSiteDebug = new EfCoreCallSiteLog(Logger(_callSiteDebugStream, LogLevel.Debug));
        _failure = Thrown();

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    // ── EfCoreDisabled: LogQueryCompleted at Debug, the logger at Information ──

    [Benchmark(Baseline = true), BenchmarkCategory("EfCoreDisabled")]
    public void EfCore_Disabled_LoggerMessage() => _microsoft.LogQueryCompleted(QueryType, TotalCount, Page, PageSize);

    [Benchmark, BenchmarkCategory("EfCoreDisabled")]
    public void EfCore_Disabled_CallSite() => _callSite.LogQueryCompleted(QueryType, TotalCount, Page, PageSize);

    // ── EfCoreDebugJson: LogQueryCompleted as a JSON line, the logger at Debug ──

    [Benchmark(Baseline = true), BenchmarkCategory("EfCoreDebugJson")]
    public void EfCore_DebugJson_LoggerMessage() => _microsoftDebug.LogQueryCompleted(QueryType, TotalCount, Page, PageSize);

    [Benchmark, BenchmarkCategory("EfCoreDebugJson")]
    public void EfCore_DebugJson_CallSite() => _callSiteDebug.LogQueryCompleted(QueryType, TotalCount, Page, PageSize);

    // ── EfCoreErrorJson: LogQueryFailed with its exception as a JSON line ──

    [Benchmark(Baseline = true), BenchmarkCategory("EfCoreErrorJson")]
    public void EfCore_ErrorJson_LoggerMessage() => _microsoft.LogQueryFailed(QueryType, ErrorType, _failure);

    [Benchmark, BenchmarkCategory("EfCoreErrorJson")]
    public void EfCore_ErrorJson_CallSite() => _callSite.LogQueryFailed(QueryType, ErrorType, _failure);

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
        return provider.GetRequiredService<ILoggerFactory>().CreateLogger("Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor");
    }

    // A failure as a provider raises one: thrown, so that it carries a stack trace for the sink to write.
    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("The connection was closed while reading the page");
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

        EfCore_DebugJson_LoggerMessage();
        EfCore_DebugJson_CallSite();
        CompareLines("DebugJson", _microsoftDebugStream.LastLine, _callSiteDebugStream.LastLine,
            "Query 'OrdersByCustomerQuery' completed: 1342 total record(s), page 3/50", failures);

        EfCore_ErrorJson_LoggerMessage();
        EfCore_ErrorJson_CallSite();
        var microsoft = _microsoftInformationStream.LastLine;
        var callSite = _callSiteInformationStream.LastLine;
        CompareLines("ErrorJson", microsoft, callSite, "Query 'OrdersByCustomerQuery' failed with Database", failures);
        foreach (var (row, line) in new[] { ("[LoggerMessage]", microsoft), ("call site", callSite) })
        {
            if (!line.Contains("The connection was closed while reading the page", StringComparison.Ordinal))
                failures.Add($"  ErrorJson {row,-16} wrote no exception: {line}");
        }

        EfCore_Disabled_LoggerMessage();
        EfCore_Disabled_CallSite();
        if (_microsoftInformationStream.LastLine != microsoft)
            failures.Add($"  [LoggerMessage] LogQueryCompleted wrote at Debug: {_microsoftInformationStream.LastLine}");
        if (_callSiteInformationStream.LastLine != callSite)
            failures.Add($"  call site       LogQueryCompleted wrote at Debug: {_callSiteInformationStream.LastLine}");

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "The EF Core rows do not do the same work:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
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
