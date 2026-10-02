namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Specifies the operation mode for a mutation.
///     Determines whether the MutationInvoker creates a new entity or loads an existing one.
/// </summary>
[Flags]
public enum MutationMode
{
    /// <summary>
    ///     Creates a new entity instance: the generated invoker calls <c>new TEntity()</c>. There is
    ///     no factory to look up.
    /// </summary>
    Create = 1,

    /// <summary>
    ///     Loads an existing entity by ID. The invoker resolves the entity from the repository
    ///     using the mutation's <c>Id</c> property.
    /// </summary>
    Update = 2,

    /// <summary>
    ///     The invoker decides at runtime: if <c>Id</c> is provided and non-default,
    ///     loads the entity (Update); otherwise, creates a new one (Create).
    /// </summary>
    /// <remarks>
    ///     ⚠️ Creation is the fallback of a <b>missing id</b>, not of a failed load. An id the caller
    ///     sent is a reference to a row, so an id that names nothing is a <c>NotFoundError</c>. This
    ///     summary already said so while the invoker asked only whether the mode carried
    ///     <see cref="Create" />: an invented id answered 200 and wrote a new row, with an id
    ///     different from the one sent.
    /// </remarks>
    CreateOrUpdate = Create | Update,

    /// <summary>
    ///     Deletes an existing entity by ID. The invoker loads the entity from the repository
    ///     and then removes it (hard delete) or marks it as deleted (soft delete).
    /// </summary>
    Delete = 4,

    /// <summary>
    ///     Restores a soft-deleted entity by ID. The invoker disables the soft-delete query filter,
    ///     loads the entity, and resets <c>IsDeleted</c>, <c>DeletedAt</c>, and <c>DeletedBy</c>.
    ///     Only valid for entities with <c>[SoftDelete]</c>.
    /// </summary>
    Restore = 8
}
