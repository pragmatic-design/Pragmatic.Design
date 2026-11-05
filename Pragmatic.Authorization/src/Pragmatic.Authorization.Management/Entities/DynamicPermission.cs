using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     A permission created at runtime (not declared with [assembly: Permission] at compile time).
///     Stored in the database for dynamic RBAC management.
/// </summary>
/// <remarks>
///     A name is unique in its tenant, enforced by the database as well as by <c>CreatePermission</c>'s check, unfiltered and with the
///     same NULL rule for a global permission as <see cref="DynamicRole" />.
/// </remarks>
[Entity]
[Unique(nameof(Name), nameof(TenantId))]
public partial class DynamicPermission : IAuditable, ISoftDelete, IEntity
{
    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Permission name (e.g., "custom.reports.export"). Must be unique.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>Logical category for grouping.</summary>
    public string? Category { get; set; }

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
