using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
///     Microsoft [LoggerMessage] source-generated call sites shared by the SourceGenerated benchmark
///     category.
/// </summary>
/// <remarks>
///     Written fully qualified: this project uses Pragmatic call sites, where the simple name binds to
///     Pragmatic's attribute, and these rows measure Microsoft's.
/// </remarks>
internal static partial class BenchmarkLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} processed for user {UserId}")]
    public static partial void OrderProcessed(ILogger logger, int orderId, string userId);
}
