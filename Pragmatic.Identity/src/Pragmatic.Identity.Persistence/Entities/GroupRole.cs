using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     Join entity mapping a group to a role with temporal validity.
///     Enables the chain: Group → Roles → Permissions.
/// </summary>
public class GroupRole : ITemporalRelation
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Group name (case-insensitive).</summary>
    public required string GroupName { get; set; }

    /// <summary>Role name (case-insensitive).</summary>
    public required string RoleName { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
