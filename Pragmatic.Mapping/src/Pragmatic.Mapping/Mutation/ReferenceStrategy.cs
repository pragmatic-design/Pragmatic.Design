namespace Pragmatic.Mapping.Mutation;

/// <summary>
///     Defines how a single reference navigation is written back to the entity.
/// </summary>
/// <remarks>
///     The twin of <see cref="CollectionStrategy" /> for the child that is one rather than many. A
///     collection had four ways of being written and a reference had one, hardcoded, so "remove the
///     address" was not expressible at all.
/// </remarks>
public enum ReferenceStrategy
{
    /// <summary>
    ///     Merge: update the child that is there, build the one that is not, and leave it alone when
    ///     the DTO sends null.
    /// </summary>
    /// <remarks>
    ///     The default, and the conservative reading: a null means "I am not telling you about this
    ///     one", the same thing it means for a scalar. A caller who wants the child gone says so with
    ///     <see cref="Detach" />, on a shape that means it.
    /// </remarks>
    Merge,

    /// <summary>
    ///     Detach: a null in the DTO removes the link.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It removes the <b>link</b>, not necessarily the row. What happens to the child is EF's
    ///     relationship configuration talking: a required or owned relationship cascades the delete,
    ///     an optional one leaves an orphan with a null foreign key. That is the same division of
    ///     labour <see cref="CollectionStrategy.Sync" /> already has — the framework drops the child
    ///     from the parent, the model decides the row's fate.
    /// </remarks>
    Detach,

    /// <summary>
    ///     Replace: build a new child from the DTO whatever is already there.
    /// </summary>
    /// <remarks>
    ///     Every existing child is discarded and rebuilt, so identity, audit columns and anything
    ///     pointing at the old row go with it. Correct when the navigation genuinely holds a value
    ///     rather than a row with a life of its own; almost never what an update means.
    /// </remarks>
    Replace,

    /// <summary>
    ///     Ignore: the navigation is not written at all, whatever the DTO carries.
    /// </summary>
    /// <remarks>
    ///     For a shape that exposes a child for reading and must never write it back. Without it the
    ///     only way to say "read-only navigation" was to leave the property off the DTO, which also
    ///     removed it from the response.
    /// </remarks>
    Ignore
}
