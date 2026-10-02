using Pragmatic.Result;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Extension methods for <see cref="IUnitOfWork" /> providing transaction scope helpers.
/// </summary>
/// <remarks>
///     <para>
///         <b>Guidance:</b>
///         Use <c>repo.SaveChangesAsync()</c> for single-entity operations where an implicit
///         transaction is sufficient. Use <c>uow.ExecuteInTransactionAsync()</c> for multi-entity
///         or multi-repository operations that require an explicit transaction with automatic
///         commit/rollback semantics.
///     </para>
///     <para>
///         Each runs inside <see cref="IUnitOfWork.ExecuteAsync{T}" />, because a retrying execution
///         strategy refuses a transaction opened outside it. A transient failure runs the operation again
///         from a cleared change tracker, so it must have no effect outside its transaction.
///     </para>
/// </remarks>
public static class UnitOfWorkExtensions
{
    /// <param name="uow">The unit of work.</param>
    extension(IUnitOfWork uow)
    {
        /// <summary>
        ///     Executes an operation within an explicit transaction, committing on success
        ///     and rolling back on failure or exception.
        /// </summary>
        /// <typeparam name="T">The success value type.</typeparam>
        /// <typeparam name="TError">The error type.</typeparam>
        /// <param name="operation">
        ///     The operation to execute. Receives a <see cref="CancellationToken" /> and must
        ///     return a <see cref="Result{T, TError}" />. The caller is responsible for calling
        ///     <see cref="IUnitOfWork.SaveChangesAsync" /> within the operation if needed.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        /// <remarks>
        ///     <para>
        ///         The transaction is committed only when the operation returns a successful result.
        ///         If the result is a failure, or if an exception is thrown, the transaction is rolled back.
        ///     </para>
        ///     <para>
        ///         Example:
        ///         <code>
        ///         var result = await uow.ExecuteInTransactionAsync&lt;OrderId, DomainError&gt;(async ct =>
        ///         {
        ///             orderRepo.Add(order);
        ///             inventoryRepo.Update(inventory);
        ///             await uow.SaveChangesAsync(ct);
        ///             return order.PersistenceId;
        ///         }, cancellationToken);
        ///         </code>
        ///     </para>
        /// </remarks>
        public async Task<Result<T, TError>> ExecuteInTransactionAsync<T, TError>(Func<CancellationToken, Task<Result<T, TError>>> operation,
            CancellationToken ct = default)
            where TError : IError
        {
            Ensure.Ensure.ThrowIfNull(uow);
            Ensure.Ensure.ThrowIfNull(operation);

            return await uow.ExecuteAsync(async token =>
            {
                var tx = await uow.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (tx.ConfigureAwait(false))
                {
                    try
                    {
                        var result = await operation(token).ConfigureAwait(false);

                        if (result.IsSuccess)
                            await tx.CommitAsync(token).ConfigureAwait(false);
                        else
                            await tx.RollbackAsync(token).ConfigureAwait(false);

                        return result;
                    }
                    catch
                    {
                        try
                        { await tx.RollbackAsync(token).ConfigureAwait(false); }
                        catch { /* Swallow — original exception is more important */ }
                        throw;
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        ///     Executes a void operation within an explicit transaction, committing on success
        ///     and rolling back on failure or exception.
        /// </summary>
        /// <typeparam name="TError">The error type.</typeparam>
        /// <param name="operation">
        ///     The operation to execute. Receives a <see cref="CancellationToken" /> and must
        ///     return a <see cref="VoidResult{TError}" />. The caller is responsible for calling
        ///     <see cref="IUnitOfWork.SaveChangesAsync" /> within the operation if needed.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The void result of the operation.</returns>
        /// <remarks>
        ///     <para>
        ///         Use this overload for operations that don't return a value but can fail with a typed error.
        ///     </para>
        ///     <para>
        ///         Example:
        ///         <code>
        ///         var result = await uow.ExecuteInTransactionAsync&lt;DomainError&gt;(async ct =>
        ///         {
        ///             orderRepo.Remove(order);
        ///             auditRepo.Add(auditEntry);
        ///             await uow.SaveChangesAsync(ct);
        ///             return VoidResult&lt;DomainError&gt;.Success();
        ///         }, cancellationToken);
        ///         </code>
        ///     </para>
        /// </remarks>
        public async Task<VoidResult<TError>> ExecuteInTransactionAsync<TError>(Func<CancellationToken, Task<VoidResult<TError>>> operation,
            CancellationToken ct = default)
            where TError : IError
        {
            Ensure.Ensure.ThrowIfNull(uow);
            Ensure.Ensure.ThrowIfNull(operation);

            return await uow.ExecuteAsync(async token =>
            {
                var tx = await uow.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (tx.ConfigureAwait(false))
                {
                    try
                    {
                        var result = await operation(token).ConfigureAwait(false);

                        if (result.IsSuccess)
                            await tx.CommitAsync(token).ConfigureAwait(false);
                        else
                            await tx.RollbackAsync(token).ConfigureAwait(false);

                        return result;
                    }
                    catch
                    {
                        try
                        { await tx.RollbackAsync(token).ConfigureAwait(false); }
                        catch { /* Swallow — original exception is more important */ }
                        throw;
                    }
                }
            }, ct).ConfigureAwait(false);
        }

        /// <summary>
        ///     Executes an operation within an explicit transaction, committing on completion
        ///     and rolling back on exception.
        /// </summary>
        /// <typeparam name="T">The return value type.</typeparam>
        /// <param name="operation">
        ///     The operation to execute. Receives a <see cref="CancellationToken" /> and returns
        ///     a raw value (not wrapped in Result). The caller is responsible for calling
        ///     <see cref="IUnitOfWork.SaveChangesAsync" /> within the operation if needed.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The value returned by the operation.</returns>
        /// <remarks>
        ///     <para>
        ///         Use this overload for simple operations that don't use the Result pattern.
        ///         The transaction commits when the operation completes successfully.
        ///         Any exception causes an automatic rollback and is re-thrown.
        ///     </para>
        ///     <para>
        ///         Example:
        ///         <code>
        ///         var orderId = await uow.ExecuteInTransactionAsync(async ct =>
        ///         {
        ///             orderRepo.Add(order);
        ///             await uow.SaveChangesAsync(ct);
        ///             return order.PersistenceId;
        ///         }, cancellationToken);
        ///         </code>
        ///     </para>
        /// </remarks>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            Ensure.Ensure.ThrowIfNull(uow);
            Ensure.Ensure.ThrowIfNull(operation);

            return await uow.ExecuteAsync(async token =>
            {
                var tx = await uow.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (tx.ConfigureAwait(false))
                {
                    try
                    {
                        var result = await operation(token).ConfigureAwait(false);
                        await tx.CommitAsync(token).ConfigureAwait(false);
                        return result;
                    }
                    catch
                    {
                        try
                        { await tx.RollbackAsync(token).ConfigureAwait(false); }
                        catch { /* Swallow — original exception is more important */ }
                        throw;
                    }
                }
            }, ct).ConfigureAwait(false);
        }
    }
}
