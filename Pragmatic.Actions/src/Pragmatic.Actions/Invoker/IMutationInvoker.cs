using Pragmatic.Actions.Mutation;
using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Invokes a mutation through the full MutationInvoker pipeline:
///     validate → load/create → apply → entity validate → persist → events.
/// </summary>
/// <typeparam name="TMutation">The mutation type.</typeparam>
/// <typeparam name="TEntity">The entity type being modified.</typeparam>
/// <remarks>
///     This interface is implemented by generated invoker classes.
///     Each mutation gets its own invoker that handles the full lifecycle.
/// </remarks>
public interface IMutationInvoker<in TMutation, TEntity>
    where TMutation : Mutation<TEntity>
    where TEntity : class
{
    /// <summary>
    ///     Invokes the mutation through the pipeline.
    /// </summary>
    /// <param name="mutation">The mutation instance with parameters set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The modified entity on success, or an error.</returns>
    Task<Result<TEntity, IError>> InvokeAsync(TMutation mutation, CancellationToken ct = default);

    /// <summary>
    ///     Invokes the mutation against an entity the caller already holds, skipping the load.
    /// </summary>
    /// <param name="mutation">The mutation instance with parameters set.</param>
    /// <param name="entity">
    ///     The entity to apply it to. It must have been read through the repository, so the global query
    ///     filters — tenant, soft delete, ownership — have already had their say.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The modified entity on success, or an error.</returns>
    /// <remarks>
    ///     <para>
    ///         The other overload loads by id, which is right when the id is all the caller has. It is a
    ///         wasted round trip when it is not: an action that loaded an entity to check something and
    ///         then mutates it pays twice, and a sweep that loaded a hundred rows pays a hundred and one
    ///         times. That cost was the whole argument for writing to the repository by hand instead —
    ///         which loses the permission, the validation and the register entry along with the query.
    ///     </para>
    ///     <para>
    ///         <b>Handed over rather than looked up on the caller's behalf.</b> EF's change tracker
    ///         already holds what this scope has read, but resolving from it silently would return rows
    ///         a filtered read would have hidden — an entity fetched on an admin path with
    ///         <c>IgnoreQueryFilters</c> would reach a mutation on an ordinary one. Passing it makes the
    ///         claim visible at the call site, where the reader can check it.
    ///     </para>
    ///     <para>
    ///         Not for a <see cref="MutationMode.Create" /> mutation: creating is what that mode does, so
    ///         supplying an entity contradicts it and throws rather than quietly updating instead.
    ///     </para>
    /// </remarks>
    Task<Result<TEntity, IError>> InvokeAsync(
        TMutation mutation, TEntity entity, CancellationToken ct = default);
}
