namespace Pragmatic.Persistence.Query.Builder;

/// <summary>
///     Applies the hints a <see cref="QueryBuilder{TEntity}" /> collected to a queryable.
/// </summary>
/// <remarks>
///     <para>
///         Declared here, where there is no EF Core, and implemented where there is — the same shape
///         as <c>INavigationLoader</c> and <c>EfMutationHelpers</c>. <c>AsNoTracking</c> and
///         <c>AsSplitQuery</c> are EF Core extension methods, so the builder cannot call them; what it
///         can do is say what was asked and let whoever has the context carry it out.
///     </para>
///     <para>
///         ⚠️ Before this, the two fluent methods set a field nobody read. A caller writing
///         <c>builder.AsNoTracking()</c> got the opposite of what it asked for, silently — measured in
///         conformance. Removing them was the wrong answer: «declared and never read» says the
///         implementation is missing, not that the capability is unwanted.
///     </para>
/// </remarks>
public interface IQueryHintApplier
{
    /// <param name="source">The queryable being built.</param>
    /// <param name="noTracking">Whether the caller asked for no change tracking.</param>
    /// <param name="splitQuery">Whether the caller asked for split queries.</param>
    IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> source, bool noTracking, bool splitQuery)
        where TEntity : class;
}
