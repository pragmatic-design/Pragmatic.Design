using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Result.Http;

namespace Pragmatic.Result.EntityFrameworkCore;

/// <summary>
///     Extension methods for IQueryable that return Result types.
/// </summary>
/// <remarks>
///     <para>
///         These extensions provide a fluent way to query entities and get Result types
///         instead of throwing exceptions or returning null.
///     </para>
///     <para>
///         On success, returns the entity. On not found, returns NotFoundError with entity type info.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var result = await dbContext.Users
///     .Where(u => u.Id == id)
///     .FirstOrDefaultAsResultAsync("User");
///
/// return result.Match(
///     onSuccess: user => Ok(user),
///     onFailure: error => NotFound(error));
/// </code>
/// </example>
public static class QueryableResultExtensions
{
    /// <param name="source">The queryable source</param>
    /// <typeparam name="T">The entity type</typeparam>
    extension<T>(IQueryable<T> source) where T : class
    {
        /// <summary>
        ///     Returns the first element, or a NotFoundError if the sequence is empty.
        /// </summary>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        public async Task<Result<T, NotFoundError>> FirstOrDefaultAsResultAsync(string entityName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await source.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create(entityName);
        }

        /// <summary>
        ///     Returns the first element matching the predicate, or a NotFoundError.
        /// </summary>
        /// <param name="predicate">The filter predicate</param>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        public async Task<Result<T, NotFoundError>> FirstOrDefaultAsResultAsync(Expression<Func<T, bool>> predicate,
            string entityName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await source.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create(entityName);
        }

        /// <summary>
        ///     Returns the single element, or a NotFoundError if the sequence is empty.
        /// </summary>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        /// <remarks>
        ///     Mirrors LINQ <c>SingleOrDefault</c> semantics: an empty sequence is an expected outcome
        ///     mapped to <see cref="NotFoundError" />, but <em>more than one</em> match throws
        ///     <see cref="InvalidOperationException" />. That is by design — "single" asserts an
        ///     at-most-one invariant, and violating it is a bug in the caller's query, not a
        ///     recoverable domain error. It is intentionally not converted to a Result. Use
        ///     <c>FirstOrDefaultAsResultAsync</c> when more than one match is legitimately possible.
        /// </remarks>
        public async Task<Result<T, NotFoundError>> SingleOrDefaultAsResultAsync(string entityName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await source.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create(entityName);
        }

        /// <summary>
        ///     Returns the single element matching the predicate, or a NotFoundError.
        /// </summary>
        /// <param name="predicate">The filter predicate</param>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        /// <remarks>
        ///     Mirrors LINQ <c>SingleOrDefault</c> semantics: no match maps to
        ///     <see cref="NotFoundError" />, but more than one match throws
        ///     <see cref="InvalidOperationException" /> by design (a violated at-most-one invariant is
        ///     a caller bug, not a recoverable domain error, so it is not converted to a Result).
        /// </remarks>
        public async Task<Result<T, NotFoundError>> SingleOrDefaultAsResultAsync(Expression<Func<T, bool>> predicate,
            string entityName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await source.SingleOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create(entityName);
        }
    }

    /// <param name="dbSet">The DbSet to search</param>
    /// <typeparam name="T">The entity type</typeparam>
    extension<T>(DbSet<T> dbSet) where T : class
    {
        /// <summary>
        ///     Finds an entity by its primary key, returning NotFoundError if not found.
        /// </summary>
        /// <typeparam name="TKey">The key type</typeparam>
        /// <param name="id">The primary key value</param>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        public async Task<Result<T, NotFoundError>> FindAsResultAsync<TKey>(TKey id,
            string entityName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dbSet);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await dbSet.FindAsync([id], cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create<TKey>(entityName, id!);
        }

        /// <summary>
        ///     Finds an entity by composite primary key, returning NotFoundError if not found.
        /// </summary>
        /// <param name="entityName">The entity type name for error reporting</param>
        /// <param name="cancellationToken">Cancellation token, propagated to the underlying query</param>
        /// <param name="keyValues">The composite key values</param>
        /// <returns>Result with the entity or NotFoundError</returns>
        /// <remarks>
        ///     The token precedes the <c>params</c> array because C# requires the parameter array to be
        ///     last; pass it explicitly, e.g. <c>FindAsResultAsync("Order", ct, tenantId, orderId)</c>.
        /// </remarks>
        public async Task<Result<T, NotFoundError>> FindAsResultAsync(string entityName,
            CancellationToken cancellationToken,
            params object?[]? keyValues)
        {
            ArgumentNullException.ThrowIfNull(dbSet);
            ArgumentNullException.ThrowIfNull(entityName);

            var entity = await dbSet.FindAsync(keyValues, cancellationToken).ConfigureAwait(false);

            return entity is not null
                ? entity
                : NotFoundError.Create(entityName);
        }
    }
}
