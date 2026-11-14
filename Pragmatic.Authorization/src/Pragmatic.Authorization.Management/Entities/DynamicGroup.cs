using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     A group created at runtime (#726): a name its members are assigned to (<see cref="UserGroupAssignment" />) and its roles are given to
///     (<see cref="GroupRoleAssignment" />), so a member holds the group's roles while both assignments are in force.
/// </summary>
/// <remarks>
///     A name is unique in its tenant, as a role's is (<see cref="DynamicRole" />), by the database as well as by <c>CreateGroup</c>'s check.
///     A global group's <see cref="TenantId" /> is NULL, which PostgreSQL, SQLite and MySQL hold distinct in a unique index, so there two
///     global groups of one name are refused by the check alone.
/// </remarks>
[Entity]
[Unique(nameof(Name), nameof(TenantId))]
public partial class DynamicGroup : IAuditable, ISoftDelete, IEntity
{
    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Group name (e.g., "night-shift"). Unique in its tenant.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string Name { get; init; }

    /// <summary>Human-readable description.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Tenant scope. Null = global (platform-level).</summary>
    [Pragmatic.Validation.Attributes.MaxLength(128)]
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
