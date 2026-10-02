namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities with creator ownership.
///     The <see cref="OwnerId"/> is automatically set from <c>ICurrentUser.Id</c> on creation.
/// </summary>
public interface IOwnedEntity
{
    /// <summary>
    ///     Gets the identifier of the user who created/owns this entity.
    /// </summary>
    string OwnerId { get; }
}
