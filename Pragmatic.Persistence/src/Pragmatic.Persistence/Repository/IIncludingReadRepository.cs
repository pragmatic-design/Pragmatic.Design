using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     A read by key with navigations, the one form every engine with navigations serves: the Sql engine's repository implements it with one
///     compiled read per set of include paths its compilation declares (<c>[LoadEntity(Include = …)]</c>), and EF Core's with an
///     <c>Include</c> per path on <c>Query()</c> (#887).
/// </summary>
/// <remarks>
///     <para>
///         The repository is registered as <see cref="IReadRepository{TEntity}" />; this is the same instance, cast, as the generated loads
///         cast it. The documents engine keeps a relation as its key, so it has no navigation to load and refuses at build (PRAG3111).
///     </para>
///     <para>
///         On EF Core the paths are strings, so a navigation filter that rewrites a lambda <c>Include</c> (<c>FilterMapComposer</c>) does
///         not reach them: the root's filters and EF Core's named query filters apply, as they do to the generated loads, which include the
///         same way.
///     </para>
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
