using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Gives a role to a group with temporal validity (#726): every member of the group holds the role while this and their membership are
///     in force.
/// </summary>
[Entity]
[Index(nameof(GroupName), nameof(RoleName), nameof(TenantId))] // the lookup of a group's roles; not unique: an ended assignment is kept
public partial class GroupRoleAssignment : ITemporalRelation, IEntity
{
    /// <summary>The group name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string GroupName { get; init; }

    /// <summary>The role name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string RoleName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>Who gave the role to the group.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
