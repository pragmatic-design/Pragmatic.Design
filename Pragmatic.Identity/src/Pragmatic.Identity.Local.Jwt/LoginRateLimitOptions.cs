using System.ComponentModel.DataAnnotations;

namespace Pragmatic.Identity.Local.Jwt;

/// <summary>
///     Options for IP-based rate limiting of the local sign-in endpoints — defense in
///     depth against volumetric credential-spraying, on top of the per-account lockout.
/// </summary>
/// <remarks>
///     Read from the configuration section <c>Identity:Local:RateLimit</c> by <c>UseJwtAuthentication</c>.
///     Active by default; set <see cref="Enabled"/> to <c>false</c> to opt out. Which endpoints it limits is
///     not an option: the exposed <c>LoginUser</c> and <c>SignInUser</c>, on whatever route the application
///     gave them.
/// </remarks>
public sealed class LoginRateLimitOptions
{
    /// <summary>Whether login rate limiting is applied. Default: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum login requests permitted per client IP within <see cref="Window"/>. Default: 10.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "PermitLimit must be at least 1.")]
    public int PermitLimit { get; set; } = 10;

    /// <summary>
    ///     Fixed time window over which <see cref="PermitLimit"/> is counted. Default: 1 minute.
    ///     Must be greater than zero and at most one day; enforced at startup.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>HTTP status code returned when the sign-in limit is exceeded. Default: 429.</summary>
    public int RejectionStatusCode { get; set; } = 429;
}
