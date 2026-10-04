using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
///     Microsoft [LoggerMessage] source-generated call sites shared by the SourceGenerated benchmark
///     category — the hot-path pattern Pragmatic.Logging recommends.
/// </summary>
internal static partial class BenchmarkLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} processed for user {UserId}")]
    public static partial void OrderProcessed(ILogger logger, int orderId, string userId);
}
