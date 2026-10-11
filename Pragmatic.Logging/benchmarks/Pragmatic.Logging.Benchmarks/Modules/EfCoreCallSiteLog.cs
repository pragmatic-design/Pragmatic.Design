using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     Two of <c>EfCoreQueryExecutor</c>'s methods, generated as Pragmatic call sites: <c>LogQueryCompleted</c>,
///     called on every paged read, and <c>LogQueryFailed</c>, called on every read that fails.
/// </summary>
internal sealed partial class EfCoreCallSiteLog(ILogger logger)
{
    public const string QueryCompletedTemplate = "Query '{queryType}' completed: {totalCount} total record(s), page {page}/{pageSize}";
    public const string QueryFailedTemplate = "Query '{queryType}' failed with {errorType}";

    private readonly ILogger _logger = logger;

    [LoggerMessage(Level = LogLevel.Debug, Message = QueryCompletedTemplate)]
    public partial void LogQueryCompleted(string queryType, int totalCount, int page, int pageSize);

    [LoggerMessage(Level = LogLevel.Error, Message = QueryFailedTemplate)]
    public partial void LogQueryFailed(string queryType, string errorType, Exception ex);
}
