namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Configuration for maintenance mode behavior.
/// </summary>
public sealed class MaintenanceModeOptions
{
    /// <summary>
    ///     Gets or sets whether to enter maintenance mode on startup failure.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     Also switched off from configuration: <c>Pragmatic:MaintenanceMode:EnableOnStartupFailure</c>
    ///     set to <c>false</c>. The key can only turn the behaviour off — it is read for that one value —
    ///     so it is how a test host makes a startup failure fail the test instead of answering 503.
    /// </remarks>
    public bool EnableOnStartupFailure { get; set; } = true;

    /// <summary>
    ///     Gets or sets the health check endpoint path.
    ///     Default is "/health".
    /// </summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>
    ///     Gets or sets the maintenance info endpoint path.
    ///     Default is "/maintenance".
    /// </summary>
    public string MaintenancePath { get; set; } = "/maintenance";

    /// <summary>
    ///     Gets or sets whether to include stack traces in maintenance response.
    ///     Default is false (only enabled automatically in Development).
    /// </summary>
    public bool? IncludeStackTrace { get; set; }

    /// <summary>
    ///     Gets or sets the base path for admin maintenance endpoints.
    ///     Default is "/admin/maintenance".
    /// </summary>
    public string AdminPath { get; set; } = "/admin/maintenance";

    /// <summary>
    ///     Gets or sets the API key for admin endpoints.
    ///     Sent via X-Maintenance-Key header. If null, only localhost requests are allowed.
    /// </summary>
    public string? AdminApiKey { get; set; }

    /// <summary>
    ///     Gets or sets whether to include detailed migration progress in status responses.
    ///     Default is true.
    /// </summary>
    public bool IncludeDetailedProgress { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether this host accepts being put into maintenance from the outside.
    ///     Default is false; <c>UseMaintenanceMode()</c> sets it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Maintenance is reached two ways, and this gates only one of them. A failure during
    ///         startup puts the host behind the maintenance page regardless — that path is a safety
    ///         net and is not negotiable. What this controls is the <b>voluntary</b> path: an
    ///         <c>EnterMaintenanceCommand</c> arriving from the control plane while the host is
    ///         serving traffic, which is how an operator drains it before a migration.
    ///     </para>
    ///     <para>
    ///         Left false, that command is refused and logged rather than ignored. A host that never
    ///         called <c>UseMaintenanceMode()</c> has no page and no progress stream to show, so
    ///         draining it would hand every caller a bare 503 with nothing behind it.
    ///     </para>
    /// </remarks>
    public bool EnableRuntimeMaintenance { get; set; }

    /// <summary>
    ///     Gets or sets whether the maintenance admin endpoints — status, SSE progress stream,
    ///     health, HTML panel and restart — are mapped under <see cref="AdminPath" />.
    ///     Default is false.
    /// </summary>
    /// <remarks>
    ///     The 503 middleware is wired into every host unconditionally, because a maintenance flag
    ///     that lets traffic through is worse than no maintenance mode at all. These endpoints are
    ///     the opposite case: they add routes and a restart verb to the host's public surface, so
    ///     they stay off until a host asks for them via
    ///     <c>UseMaintenanceMode(o =&gt; o.EnableAdminEndpoints = true)</c>.
    /// </remarks>
    public bool EnableAdminEndpoints { get; set; }
}
