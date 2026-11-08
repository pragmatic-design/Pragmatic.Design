using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Assigns a user to a group with temporal validity.
/// </summary>
[Entity]
[Index(nameof(UserId), nameof(GroupName), nameof(TenantId))] // the lookup of a user's groups; not unique: an ended assignment is kept
public partial class UserGroupAssignment : ITemporalRelation, IEntity
{
    /// <summary>The user identifier.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string UserId { get; init; }

    /// <summary>The group name.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public required string GroupName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(128)]
    public string? TenantId { get; set; }

    /// <summary>Who assigned this group.</summary>
    [Pragmatic.Validation.Attributes.MaxLength(256)]
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
