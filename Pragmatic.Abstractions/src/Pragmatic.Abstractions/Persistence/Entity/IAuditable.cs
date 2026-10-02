namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities with audit tracking.
/// </summary>
public interface IAuditable
{
    /// <summary>
    ///     Gets or sets when the entity was created.
    /// </summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    ///     Gets or sets who created the entity.
    /// </summary>
    string? CreatedBy { get; set; }

    /// <summary>
    ///     Gets or sets when the entity was last updated.
    /// </summary>
    DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    ///     Gets or sets who last updated the entity.
    /// </summary>
    string? UpdatedBy { get; set; }
}
