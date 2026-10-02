using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Assigns a role to a user with temporal validity.
///     The role can be static (IRole) or dynamic (DynamicRole).
/// </summary>
[Entity]
public partial class UserRoleAssignment : ITemporalRelation, IEntity
{
    /// <summary>The user identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>The role name.</summary>
    public required string RoleName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    public string? TenantId { get; set; }

    /// <summary>Who assigned this role.</summary>
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
