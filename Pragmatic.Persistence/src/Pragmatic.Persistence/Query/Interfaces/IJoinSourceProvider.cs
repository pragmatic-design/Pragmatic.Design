namespace Pragmatic.Persistence.Query.Interfaces;

/// <summary>
///     Hands a query the sets of the boundary it belongs to.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The boundary is not a detail: there is one <c>DbContext</c> per boundary, and EF Core
///         cannot join two queryables that come from different instances. A declared join therefore
///         reads the target from the <b>root boundary's own</b> context — the same one the query's
///         source came from — which is what makes the two composable into one SQL statement.
///     </para>
///     <para>
///         The generated query names its boundary type, so this resolves the same keyed
///         <c>DbContext</c> registration <c>DomainActionInvoker</c> already uses. Nothing is searched
///         for and nothing is matched by name.
///     </para>
/// </remarks>
public interface IJoinSourceProvider
{
    /// <summary>The sets of the boundary <typeparamref name="TBoundary" /> owns.</summary>
    /// <typeparam name="TBoundary">The boundary type, named by the generated query.</typeparam>
    IJoinSources ForBoundary<TBoundary>() where TBoundary : class;
}
