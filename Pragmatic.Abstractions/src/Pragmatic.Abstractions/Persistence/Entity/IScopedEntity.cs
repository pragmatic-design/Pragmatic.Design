namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities with scope-based data access control.
///     <see cref="AccessScopes"/> contains the list of scope identifiers
///     (e.g., <c>"user:alice"</c>, <c>"role:manager"</c>, <c>"scope:team-a"</c>)
///     that determine who can see this entity.
/// </summary>
public interface IScopedEntity
{
    /// <summary>
    ///     Gets the list of scope identifiers that grant access to this entity.
    ///     Mutable <see cref="List{T}"/> by design: <c>ScopeMaterializer</c> needs Add/Remove at runtime.
    /// </summary>
    List<string> AccessScopes { get; }
}
