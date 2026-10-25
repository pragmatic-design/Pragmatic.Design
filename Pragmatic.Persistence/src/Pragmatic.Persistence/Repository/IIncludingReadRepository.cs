using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     A read by key with navigations, on an engine that compiles its reads: the Sql engine's repository implements it with one compiled read
///     per set of include paths its compilation declares (<c>[LoadEntity(Include = …)]</c>), and the generated invoker names the set.
/// </summary>
/// <remarks>
///     EF Core's repository does not implement it: on EF Core the invoker includes through <c>Query()</c>, which an engine that compiles its
///     reads does not serve.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IIncludingReadRepository<TEntity>
    where TEntity : class, IEntity
{
    /// <summary>The entity by its key with the navigations <paramref name="includes" /> names, inside the caller's filters; null when none answers.</summary>
    /// <param name="id">The entity's key.</param>
    /// <param name="includes">The include paths, comma-separated in the order declared: the key of a read the engine compiled.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TEntity?> GetByIdIncludingAsync(Guid id, string includes, CancellationToken ct = default);
}
