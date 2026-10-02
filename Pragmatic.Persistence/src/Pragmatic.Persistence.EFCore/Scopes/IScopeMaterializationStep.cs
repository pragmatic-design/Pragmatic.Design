using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Scopes;

/// <summary>
///     Materializes the data scope rules of one entity type over a context's pending changes.
/// </summary>
/// <remarks>
///     <para>
///         One step per entity carrying <c>[HasAccessScopes]</c>, emitted by the generator the way
///         <c>ComputedScopeFilter&lt;T&gt;</c> already is. It exists so <see cref="Interceptors.ScopeInterceptor" />
///         can materialize without ever asking a row for its runtime type: the generator knows which
///         entities are scoped, so the typed step is decided where it is known and
///         <c>MakeGenericType</c> on <c>entry.Entity.GetType()</c> never appears.
///     </para>
///     <para>
///         Registered as a collection: an application with no scoped entity has no steps, and the
///         interceptor's loop runs zero times.
///     </para>
/// </remarks>
public interface IScopeMaterializationStep
{
    /// <summary>
    ///     Materializes the rules of this step's entity type over the context's added and modified rows.
    /// </summary>
    /// <param name="context">The context whose change tracker is being saved.</param>
    void Materialize(DbContext context);
}
