namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Strategy that governs how the cross-request permission cache stays consistent
///     with the underlying role/permission state. Chosen by the application.
/// </summary>
public enum PermissionCacheStrategy
{
    /// <summary>
    ///     The cached permission set expires after <see cref="PermissionCacheOptions.Expiration"/>
    ///     and is re-resolved on the next request. A long expiration favors throughput; a short
    ///     one provides periodic re-validation (a role or user-disable change propagates within
    ///     the expiration window). This is the default.
    /// </summary>
    TimeToLive = 0,

    /// <summary>
    ///     The cached permission set is kept until it is invalidated explicitly through
    ///     <see cref="Pragmatic.Authorization.Evaluation.IPermissionCacheInvalidator"/> — the
    ///     application calls the invalidator whenever a user's roles, permissions, or active
    ///     status change. <see cref="PermissionCacheOptions.Expiration"/> still applies as a
    ///     safety-net upper bound. Use this for immediate propagation of authorization changes.
    /// </summary>
    ManualInvalidation = 1,
}
