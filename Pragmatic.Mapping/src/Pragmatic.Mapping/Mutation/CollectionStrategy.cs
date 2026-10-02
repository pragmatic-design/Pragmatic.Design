namespace Pragmatic.Mapping.Mutation;

/// <summary>
///     Defines how a collection navigation should be synchronized
///     when applying a mutation from DTO to entity.
/// </summary>
public enum CollectionStrategy
{
    /// <summary>
    ///     Full synchronization: add new items, update existing, remove missing.
    ///     Items are matched by key. Items in the entity collection that are not
    ///     present in the DTO collection are removed.
    /// </summary>
    Sync,

    /// <summary>
    ///     Add-only: new items from the DTO are added, existing items are updated,
    ///     but no items are removed from the entity collection.
    /// </summary>
    AddOnly,

    /// <summary>
    ///     Replace: the entire entity collection is cleared and recreated
    ///     from the DTO collection using the factory delegate.
    /// </summary>
    /// <remarks>
    ///     Every existing child is discarded and rebuilt, so identity, audit columns, soft-delete state
    ///     and anything pointing at the old rows go with them. Correct when creating; almost never what
    ///     an update means. It is here for the case where the collection genuinely is a value — a list
    ///     of tags rewritten wholesale — not as a default.
    /// </remarks>
    Replace,

    /// <summary>
    ///     Ignore: the collection is not written at all, whatever the DTO carries.
    /// </summary>
    /// <remarks>
    ///     For a shape that exposes children for reading and must never write them back. Without it the
    ///     only way to say "read-only collection" was to leave the property off the DTO, which also
    ///     removed it from the response.
    /// </remarks>
    Ignore
}
