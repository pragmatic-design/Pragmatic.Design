namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Configuration for a named rate limiter policy.
///     Used with <see cref="PragmaticEndpointsOptions.ConfigureRateLimiter"/>.
/// </summary>
public sealed class RateLimiterConfiguration
{
    /// <summary>
    ///     The rate limiting strategy to use.
    ///     Default is <see cref="RateLimiterStrategy.FixedWindow"/>.
    /// </summary>
    public RateLimiterStrategy Strategy { get; set; } = RateLimiterStrategy.FixedWindow;

    /// <summary>
    ///     Maximum number of requests permitted in the window.
    /// </summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>
    ///     Time window for the rate limiter.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Number of segments the window is divided into (sliding window only).
    ///     Default is 3.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 3;

    /// <summary>
    ///     Maximum number of queued requests when the limit is exceeded.
    ///     Default is 0 (no queueing — reject immediately).
    /// </summary>
    public int QueueLimit { get; set; }

    private int _tokensPerPeriod;

    /// <summary>
    ///     Tokens replenished per period (token bucket only).
    ///     When left at 0 (the default) the effective value falls back to
    ///     <see cref="PermitLimit"/>, matching the documented intent and avoiding
    ///     the trap where a default-constructed config silently blocks every request.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a negative value.</exception>
    public int TokensPerPeriod
    {
        get => _tokensPerPeriod > 0 ? _tokensPerPeriod : PermitLimit;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "TokensPerPeriod must be >= 0; use 0 to fall back to PermitLimit.");
            _tokensPerPeriod = value;
        }
    }

    /// <summary>
    ///     Auto-replenishment enabled (token bucket only).
    ///     Default is <c>true</c>.
    /// </summary>
    public bool AutoReplenishment { get; set; } = true;

    /// <summary>
    ///     Counts every caller against one bucket instead of one bucket per caller. Default false.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A limit is per caller — tenant, then authenticated user, then remote address — because
    ///         that is what "N requests per window" means to whoever reads it. One shared bucket used
    ///         to be the only behaviour, and in a multi-tenant deployment it let one tenant in a retry
    ///         loop refuse every other one.
    ///     </para>
    ///     <para>
    ///         Set this when the number protects something the whole application shares — a licence
    ///         seat, an outbound quota, a downstream that bills per call — rather than the caller.
    ///     </para>
    /// </remarks>
    public bool Global { get; set; }
}
