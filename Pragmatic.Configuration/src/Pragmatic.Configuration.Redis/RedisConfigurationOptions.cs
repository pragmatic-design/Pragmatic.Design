namespace Pragmatic.Configuration.Redis;

/// <summary>Options for the Redis configuration backend.</summary>
public sealed class RedisConfigurationOptions
{
    /// <summary>StackExchange.Redis connection string (e.g. <c>localhost:6379</c>).</summary>
    public string Configuration { get; set; } = "localhost:6379";

    /// <summary>
    ///     Optional key prefix (namespace) for all Pragmatic entries, joined with <c>:</c> (Redis convention).
    ///     Tenant entries nest under <c>tenants:{id}:</c>.
    /// </summary>
    public string? KeyPrefix { get; set; }
}
