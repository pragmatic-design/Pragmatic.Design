using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     Join entity mapping a role to a permission with temporal validity.
///     This replaces or supplements the in-memory <c>InMemoryRolePermissionStore</c>
///     with database-backed, time-aware permission mapping.
/// </summary>
public class RolePermission : ITemporalRelation
{
    /// <summary>Primary key.</summary>
    // Client-assigned GUID by design: no DB round-trip on insert, consistent across all
    // Identity join entities (UserRole, UserGroup, GroupRole, ExternalIdentityRecord).
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Role name (case-insensitive).</summary>
    public required string RoleName { get; set; }

    /// <summary>Permission name (e.g., "booking.guests.create").</summary>
    public required string PermissionName { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
