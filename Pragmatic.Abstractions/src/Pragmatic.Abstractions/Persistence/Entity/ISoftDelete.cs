namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities with soft delete support.
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    ///     Gets or sets whether the entity has been soft-deleted.
    /// </summary>
    bool IsDeleted { get; set; }

    /// <summary>
    ///     Gets or sets when the entity was soft-deleted.
    /// </summary>
    DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    ///     Gets or sets who soft-deleted the entity.
    ///     Populated by the repository using <c>ICurrentUser.IdOrNull()</c> when available.
    /// </summary>
    string? DeletedBy { get; set; }
}
