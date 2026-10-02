namespace Pragmatic.Gateway;

/// <summary>
///     Global fixed-window rate limiting configuration.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Maximum requests allowed per window per key. Default: 1000.</summary>
    public int PermitLimit { get; set; } = 1000;

    /// <summary>Rate-limit window duration. Default: 1 minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Key strategy for rate limiting. Supported values: <c>ip</c> (client IP, default) and
    ///     <c>tenant</c> (the <c>X-Tenant-Id</c> header). Partitions the global limiter by this key.
    /// </summary>
    public string KeyStrategy { get; set; } = "ip";
}
