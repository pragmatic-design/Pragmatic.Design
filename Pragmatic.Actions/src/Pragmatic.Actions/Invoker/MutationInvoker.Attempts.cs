using Pragmatic.Result;

namespace Pragmatic.Actions.Invoker;

public abstract partial class MutationInvoker<TMutation, TEntity>
{
    /// <summary>
    ///     Runs the mutation's attempts: once, unless the mutation declares <c>[ResiliencePolicy]</c>.
    /// </summary>
    /// <param name="attempt">One whole invocation — load, apply, validate, save, side effects.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What the last attempt returned.</returns>
    /// <remarks>
    ///     <para>
    ///         The generated invoker overrides this with the named pipeline, the way the domain-action
    ///         invoker wraps its execution. Without that override the attribute would be read, counted as
    ///         a resilience declaration, and the mutation would run once.
    ///     </para>
    ///     <para>
    ///         Called only when this invocation owns the commit. A mutation nested in one that holds its
    ///         unit of work stages its writes for the owner to save, so running it again would retry a
    ///         write that has not happened yet; the retry belongs to the unit that commits.
    ///     </para>
    /// </remarks>
    protected virtual Task<Result<TEntity, IError>> RunAttemptsAsync(
        Func<CancellationToken, Task<Result<TEntity, IError>>> attempt, CancellationToken ct)
        => attempt(ct);

    /// <summary>One attempt of the invocation, starting clean from the second one.</summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A failed attempt leaves its writes in the change tracker. The save path detaches the
    ///         root it failed on, and nothing else: children added under an optional key stay pending
    ///         and are written again by the next attempt — a duplicate per child — and a row the body
    ///         changed before throwing comes back from the next load as the tracked, already-changed
    ///         instance, so the change is applied twice. Both measured. Every attempt after
    ///         the first therefore starts from a unit of work that has forgotten what it tracked, as
    ///         <c>IUnitOfWork.ExecuteAsync</c> does for its own retries, and reads again.
    ///     </para>
    ///     <para>
    ///         That includes a handed-over entity: it carries the failed attempt's writes and is no longer
    ///         tracked, so the retry loads the row instead.
    ///     </para>
    /// </remarks>
    private Func<CancellationToken, Task<Result<TEntity, IError>>> AttemptFor(TMutation mutation, TEntity? preloaded)
    {
        var attempts = 0;

        return attemptCt =>
        {
            if (attempts++ == 0)
                return InvokeCoreAsync(mutation, commitsHere: true, preloaded, attemptCt);

            UnitOfWork?.DiscardChanges();
            return InvokeCoreAsync(mutation, commitsHere: true, preloaded: null, attemptCt);
        };
    }
}
