using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Scopes;

/// <summary>
///     Materializes data scope rules onto entities by populating their <c>AccessScopes</c>.
///     Called by the interceptor on SaveChanges and by the re-materialization background job.
/// </summary>
public interface IScopeMaterializer
{
    /// <summary>
    ///     Evaluates all registered <see cref="DataScopeRule{T}"/> rules for the given entity
    ///     and grants/revokes scopes in its <c>AccessScopes</c> accordingly.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="entity">The entity to materialize scopes for.</param>
    void Materialize<T>(T entity) where T : class, IScopedEntity;
}
