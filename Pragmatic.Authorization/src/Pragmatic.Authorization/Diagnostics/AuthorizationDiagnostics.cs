using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Authorization.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Authorization: ActivitySource and Meter with instruments.
/// </summary>
public static class AuthorizationDiagnostics
{
    /// <summary>The source name for all Authorization activities.</summary>
    public const string SourceName = "Pragmatic.Authorization";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Counter of total permission checks (HasPermission, HasAnyPermission, HasAllPermissions).</summary>
    public static readonly Counter<long> PermissionChecks = Meter.CreateCounter<long>(
        "pragmatic.authorization.permission_checks",
        description: "Total permission check count");

    /// <summary>Counter of permission checks that resulted in denial.</summary>
    public static readonly Counter<long> PermissionDenied = Meter.CreateCounter<long>(
        "pragmatic.authorization.permission_denied",
        description: "Permission denied count");

    /// <summary>Counter of permission resolution cache hits (resolved set already available in scope).</summary>
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "pragmatic.authorization.cache_hits",
        description: "Permission cache hit count");

    /// <summary>Counter of permission resolution cache misses (full provider chain invoked).</summary>
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "pragmatic.authorization.cache_misses",
        description: "Permission cache miss count");

    /// <summary>
    ///     Counter of reentrant permission resolutions — a provider asked for the permissions it is
    ///     itself resolving. Anything above zero means some provider reads through a path that
    ///     consults permissions, and is silently getting the empty set instead of its own answer.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Before the guard that increments this, the same situation was a stack overflow: the
    ///     process died with no exception and no log. The counter exists so the condition is
    ///     countable rather than fatal.
    /// </remarks>
    public static readonly Counter<long> ReentrantResolutions = Meter.CreateCounter<long>(
        "pragmatic.authorization.reentrant_resolutions",
        description: "Permission resolutions re-entered from within a permission provider");
}
