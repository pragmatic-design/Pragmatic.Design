using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     Join entity linking a user to a group with temporal validity.
/// </summary>
/// <typeparam name="TKey">The type of the user's primary key.</typeparam>
public sealed class UserGroup<TKey> : ITemporalRelation
    where TKey : notnull
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the user.</summary>
    public TKey UserId { get; set; } = default!;

    /// <summary>Group name (case-insensitive).</summary>
    public required string GroupName { get; set; }

    /// <summary>Who assigned this group membership.</summary>
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
