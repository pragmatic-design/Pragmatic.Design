namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Options for cross-request permission caching via ICacheStack.
/// </summary>
/// <remarks>
///     Three consistency patterns are available, selected by the application:
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Time-to-live</b> — <see cref="Strategy"/> = <see cref="PermissionCacheStrategy.TimeToLive"/>
///                 with a long <see cref="Expiration"/>. Best throughput; authorization changes
///                 propagate within the expiration window.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Periodic re-validation</b> — <see cref="Strategy"/> = <see cref="PermissionCacheStrategy.TimeToLive"/>
///                 with a short <see cref="Expiration"/> (e.g. 30–60s). Permissions are re-resolved
///                 frequently, so a role change or user-disable takes effect quickly.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Manual invalidation</b> — <see cref="Strategy"/> = <see cref="PermissionCacheStrategy.ManualInvalidation"/>.
///                 The application calls
///                 <see cref="Pragmatic.Authorization.Evaluation.IPermissionCacheInvalidator"/> on
///                 every authorization change for immediate propagation; <see cref="Expiration"/>
///                 acts only as a safety-net upper bound.
///             </description>
///         </item>
///     </list>
/// </remarks>
public sealed class PermissionCacheOptions
{
    /// <summary>
    ///     How long resolved permissions are cached across requests.
    ///     Default: 5 minutes.
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Prefix for cache keys. Default: <c>permissions</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "permissions";

    /// <summary>
    ///     Consistency strategy for the cached permission set.
    ///     Default: <see cref="PermissionCacheStrategy.TimeToLive"/>.
    /// </summary>
    public PermissionCacheStrategy Strategy { get; set; } = PermissionCacheStrategy.TimeToLive;
}
