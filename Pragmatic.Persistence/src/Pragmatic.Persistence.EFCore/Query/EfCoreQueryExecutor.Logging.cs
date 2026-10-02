using Microsoft.Extensions.Logging;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Source-generated structured logging for <see cref="EfCoreQueryExecutor"/>.
/// </summary>
public sealed partial class EfCoreQueryExecutor
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Query '{queryType}' executing (cached={cached})")]
    private partial void LogQueryExecuting(string queryType, bool cached);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Query '{queryType}' completed: {totalCount} total record(s), page {page}/{pageSize}")]
    private partial void LogQueryCompleted(string queryType, int totalCount, int page, int pageSize);

    [LoggerMessage(Level = LogLevel.Error, Message = "Query '{queryType}' failed with {errorType}")]
    private partial void LogQueryFailed(string queryType, string errorType, Exception ex);
}
