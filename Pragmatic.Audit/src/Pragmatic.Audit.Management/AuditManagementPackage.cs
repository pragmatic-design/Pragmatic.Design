using Pragmatic.Composition;

namespace Pragmatic.Audit.Management;

/// <summary>
///     Admin management package for the audit trail.
/// </summary>
/// <remarks>
///     Import with <c>[UsePackage&lt;AuditManagementPackage&gt;]</c> to expose the retention operation
///     under <c>/admin/audit</c>.
///     <para>
///         A registered <c>AuditRetentionService</c> that nothing calls leaves a trail documented as
///         bounded growing without limit in every deployment. Retention is a deployment decision — how long, and when — so the framework does
///         not schedule it; what it owes you is a way to trigger it.
///     </para>
/// </remarks>
public sealed class AuditManagementPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Pragmatic.Audit.Management";

    /// <inheritdoc />
    public static string? RoutePrefix => "admin/audit";

    /// <inheritdoc />
    public static string? Description => "Audit trail admin: discard sealed segments older than a retention window";
}
