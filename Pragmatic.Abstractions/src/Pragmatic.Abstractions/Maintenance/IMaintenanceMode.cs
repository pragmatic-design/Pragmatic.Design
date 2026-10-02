namespace Pragmatic.Maintenance;

/// <summary>
///     Runtime maintenance mode — can be activated during migrations, deployments, or manually.
///     Inject this to check maintenance state from any module without ASP.NET Core dependency.
/// </summary>
public interface IMaintenanceMode
{
    /// <summary>
    ///     Whether the application is currently in maintenance mode.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Human-readable reason for maintenance (e.g. "Database initialization").
    /// </summary>
    string? Reason { get; }

    /// <summary>
    ///     When maintenance mode was activated. Null if not active.
    /// </summary>
    DateTimeOffset? ActivatedAt { get; }

    /// <summary>
    ///     Estimated end time. Null if unknown.
    /// </summary>
    DateTimeOffset? EstimatedEnd { get; }

    /// <summary>
    ///     Activates maintenance mode. Dispose the returned handle to deactivate.
    /// </summary>
    /// <param name="reason">Human-readable reason for entering maintenance.</param>
    /// <param name="eta">Estimated duration. Null if unknown.</param>
    /// <returns>Disposable that deactivates maintenance mode when disposed.</returns>
    /// <remarks>
    ///     <para>
    ///         <b>Concurrency:</b> implementations must be thread-safe. Calling <c>Activate</c>
    ///         while maintenance is already active stacks activations — the mode remains active
    ///         until all returned handles are disposed (reference-counted). The last dispose
    ///         deactivates maintenance mode.
    ///     </para>
    ///     <para>
    ///         <b>Idempotency:</b> this method is not idempotent by design; each call must
    ///         return a distinct handle. Disposing the same handle twice is a no-op.
    ///     </para>
    /// </remarks>
    IDisposable Activate(string reason, TimeSpan? eta = null);
}
