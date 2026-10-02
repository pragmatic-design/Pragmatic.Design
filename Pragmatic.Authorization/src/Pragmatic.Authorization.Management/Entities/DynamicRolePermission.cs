using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Join entity mapping a dynamic role to a permission with temporal validity.
///     Supports both static (SG-generated) and dynamic permission names.
/// </summary>
[Entity]
public partial class DynamicRolePermission : ITemporalRelation, IEntity
{
    /// <summary>The role name.</summary>
    public required string RoleName { get; init; }

    /// <summary>The permission name.</summary>
    public required string PermissionName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    public string? TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
