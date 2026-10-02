using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Marker interface for query filters.
///     Used for type constraints and DI registration.
/// </summary>
public interface IQueryFilter
{
    /// <summary>
    ///     Gets the priority of this filter (lower = higher priority, applied first).
    ///     Allows the <see cref="IQueryFilterProvider"/> to order filters without knowing T.
    /// </summary>
    int Priority => 0;

    /// <summary>
    ///     The entity this filter applies to, or <c>null</c> when the implementation does not say.
    /// </summary>
    /// <remarks>
    ///     Answered by <see cref="IQueryFilter{T}" /> as <c>typeof(T)</c>. It exists so a filter can be
    ///     put into the navigation <c>FilterMap</c> — which is keyed by entity type — without
    ///     reflecting over the implementation to find its type argument.
    /// </remarks>
    Type? EntityType => null;

    /// <summary>
    ///     This filter's predicate, untyped, or <c>null</c> when the implementation does not say.
    /// </summary>
    /// <remarks>
    ///     The <c>FilterMap</c> holds <see cref="LambdaExpression" />, so the typed
    ///     <c>Expression&lt;Func&lt;T, bool&gt;&gt;</c> goes in as it is. Declared here with a default
    ///     rather than as a requirement: nothing that already implements this interface has to change.
    /// </remarks>
    LambdaExpression? GetFilterExpression() => null;

    /// <summary>
    ///     Where this filter is applied — the root of a query, collections, references.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Declared here rather than only on <see cref="IQueryFilter{T}" /> because the composition
    ///         is done once, over untyped filters, for both the root predicate and the navigation map.
    ///         A property only the typed view could see would have had to be read a second way, which
    ///         is how the two sides came to disagree in the first place.
    ///     </para>
    ///     <para>
    ///         A collection navigation is asked about by where the query reads it —
    ///         <see cref="FilterScope.Collections" />, <see cref="FilterScope.Subqueries" /> or
    ///         <see cref="FilterScope.Projections" /> — and a set a declared join reads by
    ///         <see cref="FilterScope.Joins" />. ⚠️ Optional and required references are not visited:
    ///         <c>PragmaticQueryFilterVisitor</c> rewrites collection navigations and leaves references
    ///         alone, so those two flags describe places nothing visits yet.
    ///     </para>
    /// </remarks>
    FilterScope Scope => FilterScope.Default;

    /// <summary>
    ///     Fine-grained control beyond <see cref="Scope" />: whether to apply here specifically.
    /// </summary>
    bool ShouldApplyTo(NavigationContext context) => true;
}

/// <summary>
///     Defines a global filter that is automatically applied to queries.
///     Similar to EF Core's HasQueryFilter but with more control.
/// </summary>
/// <typeparam name="T">The entity type to filter.</typeparam>
/// <remarks>
///     <para>
///         Query filters are applied automatically by the <see cref="IQueryFilterProvider"/>
///         via an Expression Visitor. The filter is applied at:
///         <list type="bullet">
///             <item>Root query (Where clause)</item>
///             <item>Include/ThenInclude operations</item>
///             <item>Join operations</item>
///             <item>Subqueries (Any, All, etc.)</item>
///         </list>
///     </para>
///     <para>
///         Use <see cref="IQueryFilter.Scope"/> to control where the filter is applied.
///         Use <see cref="IQueryFilter.ShouldApplyTo"/> for fine-grained control.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Soft-delete filter
/// public class SoftDeleteFilter&lt;T&gt; : IQueryFilter&lt;T&gt; where T : ISoftDelete
/// {
///     public Expression&lt;Func&lt;T, bool&gt;&gt; GetFilter()
///         =&gt; e =&gt; !e.IsDeleted;
/// }
///
/// // Tenant filter
/// public class TenantFilter&lt;T&gt; : IQueryFilter&lt;T&gt; where T : ITenantEntity
/// {
///     private readonly ITenantContext _tenant;
///
///     public TenantFilter(ITenantContext tenant) =&gt; _tenant = tenant;
///
///     public Expression&lt;Func&lt;T, bool&gt;&gt; GetFilter()
///         =&gt; e =&gt; e.TenantId == _tenant.CurrentTenantId;
///
///     public FilterScope Scope =&gt; FilterScope.All;
/// }
/// </code>
/// </example>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IQueryFilter<T> : IQueryFilter where T : class
{
    /// <summary>
    ///     Gets the filter expression to apply.
    /// </summary>
    /// <returns>A predicate expression that filters entities.</returns>
    Expression<Func<T, bool>> GetFilter();

    /// <summary>
    ///     Gets the scope defining where this filter should be applied.
    ///     Default is <see cref="FilterScope.Default"/>.
    /// </summary>

    /// <summary>
    ///     Gets the priority of this filter (lower = higher priority, applied first).
    ///     Default is 0. Hides <see cref="IQueryFilter.Priority"/> intentionally —
    ///     both return the same value; the generic version exists for typed access.
    /// </summary>
    new int Priority => 0;

    /// <summary>
    ///     Gets the behavior when encountering a required navigation.
    ///     Default is <see cref="FilterBehaviorOnRequired.FilterNavigation"/>.
    /// </summary>
    FilterBehaviorOnRequired BehaviorOnRequired => FilterBehaviorOnRequired.FilterNavigation;

    /// <inheritdoc />
    Type? IQueryFilter.EntityType => typeof(T);

    /// <inheritdoc />
    LambdaExpression? IQueryFilter.GetFilterExpression() => GetFilter();
}
