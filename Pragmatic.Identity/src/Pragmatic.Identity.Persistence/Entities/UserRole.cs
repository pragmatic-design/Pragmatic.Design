using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     Join entity linking a user to a role with temporal validity.
///     Immutable semantics: revocation sets <see cref="ITemporalRelation.ValidTo"/>,
///     creating an audit trail.
/// </summary>
/// <typeparam name="TKey">The type of the user's primary key.</typeparam>
public class UserRole<TKey> : ITemporalRelation
    where TKey : notnull
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the user.</summary>
    // Required FK populated by EF / the caller; 'default!' is the standard idiom for a
    // non-null TKey that has no compile-time initializer.
    public TKey UserId { get; set; } = default!;

    /// <summary>Role name (case-insensitive, matches <see cref="Pragmatic.Authorization.IRole.Name"/>).</summary>
    public required string RoleName { get; set; }

    /// <summary>Who assigned this role.</summary>
    public string? AssignedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset ValidFrom { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ValidTo { get; set; }
}
