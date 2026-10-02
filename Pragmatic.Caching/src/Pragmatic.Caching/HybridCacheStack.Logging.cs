using Microsoft.Extensions.Logging;

namespace Pragmatic.Caching;

/// <summary>
///     Source-generated structured logging for <see cref="HybridCacheStack"/>.
/// </summary>
public sealed partial class HybridCacheStack
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache get-or-set key='{key}'")]
    private partial void LogGetOrSet(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit key='{key}'")]
    private partial void LogCacheHit(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache miss key='{key}', executing factory")]
    private partial void LogCacheMiss(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache set key='{key}'")]
    private partial void LogCacheSet(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache increment key='{key}' delta={delta} new={newValue}")]
    private partial void LogCacheIncrement(string key, long delta, long newValue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache remove key='{key}'")]
    private partial void LogCacheRemove(string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache invalidate tag='{tag}'")]
    private partial void LogInvalidateTag(string tag);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache invalidating {tagCount} tag(s)")]
    private partial void LogInvalidateTags(int tagCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cache tag invalidation failed for tag='{tag}'")]
    private partial void LogTagInvalidationFailed(string tag, Exception exception);
}
