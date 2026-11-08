using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Assigns a role to a user with temporal validity.
///     The role can be static (IRole) or dynamic (DynamicRole).
/// </summary>
[Entity]
[Index(nameof(UserId), nameof(RoleName), nameof(TenantId))] // the lookup of a user's roles; not unique: an ended assignment is kept
public partial class UserRoleAssignment : ITemporalRelation, IEntity
{
    /// <summary>The user identifier.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string UserId { get; init; }

    /// <summary>The role name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string RoleName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>Who assigned this role.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
