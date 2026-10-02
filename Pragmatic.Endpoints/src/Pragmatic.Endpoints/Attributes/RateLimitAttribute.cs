namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Configures rate limiting for an endpoint.
/// </summary>
/// <remarks>
///     <para>
///         Uses ASP.NET Core's built-in rate limiter. Specify either
///         inline limits or reference a named policy.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Inline limits
/// [Endpoint(HttpVerb.Post, "/api/auth/login")]
/// [RateLimit(Requests = 5, Window = "1m")]
/// public partial class Login : Endpoint&lt;TokenResponse&gt; { }
/// 
/// // Named policy
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// [RateLimit(Policy = "standard")]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RateLimitAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the maximum number of requests allowed in the window.
    /// </summary>
    public int Requests { get; set; }

    /// <summary>
    ///     Gets or sets the time window (e.g., "1m", "1h", "1d").
    /// </summary>
    public string? Window { get; set; }

    /// <summary>
    ///     Gets or sets the named rate limit policy to use.
    /// </summary>
    /// <remarks>
    ///     When specified, <see cref="Requests" /> and <see cref="Window" /> are ignored.
    /// </remarks>
    public string? Policy { get; set; }
}