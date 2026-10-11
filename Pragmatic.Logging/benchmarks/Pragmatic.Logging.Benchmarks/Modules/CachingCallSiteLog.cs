using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks.Modules;

/// <summary>
///     Two of <c>HybridCacheStack</c>'s methods, generated as Pragmatic call sites: <c>LogGetOrSet</c>, called
///     on every read through the cache, and <c>LogTagInvalidationFailed</c>, called for every tag a
///     multi-tag invalidation could not clear.
/// </summary>
internal sealed partial class CachingCallSiteLog(ILogger logger)
{
    public const string GetOrSetTemplate = "Cache get-or-set key='{key}'";
    public const string TagInvalidationFailedTemplate = "Cache tag invalidation failed for tag='{tag}'";

    private readonly ILogger _logger = logger;

    [LoggerMessage(Level = LogLevel.Debug, Message = GetOrSetTemplate)]
    public partial void LogGetOrSet(string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = TagInvalidationFailedTemplate)]
    public partial void LogTagInvalidationFailed(string tag, Exception exception);
}
