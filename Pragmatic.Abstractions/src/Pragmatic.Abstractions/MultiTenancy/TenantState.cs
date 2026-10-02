namespace Pragmatic.MultiTenancy;

/// <summary>
///     Lifecycle state of a tenant in the system.
/// </summary>
/// <remarks>
///     Conceptual lifecycle: <see cref="Provisioning"/> → <see cref="Active"/> ↔
///     <see cref="Migrating"/> / <see cref="Suspended"/> → <see cref="Deactivated"/>.
///     Explicit numeric values are pinned so that persisted ints remain stable; that is why
///     <see cref="Provisioning"/> carries the highest value rather than the lowest, despite being
///     first in the lifecycle.
/// </remarks>
public enum TenantState
{
    /// <summary>Tenant is active and serving requests.</summary>
    Active = 0,

    /// <summary>Tenant database is being migrated.</summary>
    Migrating = 1,

    /// <summary>Tenant is suspended (e.g. payment issue, admin action).</summary>
    Suspended = 2,

    /// <summary>Tenant has been deactivated (soft-deleted, GDPR, etc.).</summary>
    Deactivated = 3,

    /// <summary>
    ///     Tenant is being created/initialized and has not yet completed its first migration.
    ///     This is the entry state in the lifecycle: a freshly registered tenant whose database
    ///     (or row-level scope) is not yet ready to serve requests. It transitions to
    ///     <see cref="Active"/> once provisioning and the initial migration succeed.
    /// </summary>
    Provisioning = 4,
}
