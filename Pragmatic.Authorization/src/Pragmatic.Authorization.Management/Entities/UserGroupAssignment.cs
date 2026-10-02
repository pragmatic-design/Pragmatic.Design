using Pragmatic.Persistence.Entity;

namespace Pragmatic.Authorization.Management.Entities;

/// <summary>
///     Assigns a user to a group with temporal validity.
/// </summary>
[Entity]
public partial class UserGroupAssignment : ITemporalRelation, IEntity
{
    /// <summary>The user identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>The group name.</summary>
    public required string GroupName { get; init; }

    /// <summary>Tenant scope. Null = global.</summary>
    public string? TenantId { get; set; }

    /// <summary>Who assigned this group.</summary>
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
