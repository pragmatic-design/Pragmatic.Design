using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Join entity mapping a dynamic role to a permission with temporal validity.
///     Supports both static (SG-generated) and dynamic permission names.
/// </summary>
/// <remarks>
///     Not unique: a grant is a temporal record, and one (role, permission, tenant) has a row per validity window. The application keeps
///     one active (<c>AssignPermissionsToRole</c> checks before inserting); the index is the lookup.
/// </remarks>
[Entity]
[Index(nameof(RoleName), nameof(PermissionName), nameof(TenantId))]
public partial class DynamicRolePermission : ITemporalRelation, IEntity
{
    /// <summary>The role name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string RoleName { get; init; }

    /// <summary>The permission name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string PermissionName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(128)]
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
