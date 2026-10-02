using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     A role created at runtime (not from IRole at compile-time).
///     Stored in the database for dynamic RBAC management.
/// </summary>
[Entity]
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
