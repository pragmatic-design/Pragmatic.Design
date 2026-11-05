using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     A role created at runtime (not from IRole at compile-time).
///     Stored in the database for dynamic RBAC management.
/// </summary>
/// <remarks>
///     A name is unique in its tenant, enforced by the database as well as by <c>CreateRole</c>'s check, which two concurrent requests
///     can both pass. The index is not filtered on <see cref="IsDeleted" />: the entity is not <c>[SoftDelete]</c>, and nothing in the
///     package deletes a role. A global role's <see cref="TenantId" /> is NULL, which PostgreSQL, SQLite and MySQL hold distinct in a
///     unique index, so there two global roles of one name are refused by the check alone.
/// </remarks>
[Entity]
[Unique(nameof(Name), nameof(TenantId))]
public partial class DynamicRole : IAuditable, ISoftDelete, IEntity
{
    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Role name (e.g., "custom-reviewer"). Must be unique.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>Tenant scope. Null = global (platform-level).</summary>
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
