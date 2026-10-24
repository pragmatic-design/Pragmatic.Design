namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The steps of an Include path below a navigation or a collection (<c>c => c.Orders.Then(o => o.Lines)</c>,
///     <c>o => o.Customer.Then(c => c.Orders)</c>, chained to any depth). The path is read by the generator or by
///     the runtime translator, never run: the chain stays one struct with one Include where EF Core has
///     ThenInclude, and the lambda shows the navigation each step descends from. The receiver is constrained
///     to <see cref="ISqlEntity" />, so the methods show on entities and their collections, not on every class.
///     A collection may be filtered on the way with LINQ's own <c>Where</c>, on columns and constants
///     (<c>c => c.Orders.Where(o => !o.IsDeleted).Then(o => o.Lines)</c>): the filter goes into the child query.
/// </summary>
public static class SqlPath
{
    /// <summary>Goes on below a to-one navigation: what <paramref name="path" /> reaches from the navigation's object.</summary>
    public static TNext Then<TNavigation, TNext>(this TNavigation? navigation, Func<TNavigation, TNext> path) where TNavigation : class, ISqlEntity => throw NotRun();

    /// <summary>Goes on below a collection, filtered or not: what <paramref name="path" /> reaches from each of its elements. ASSUMPTION: collection navigations are <see cref="List{T}" />.</summary>
    public static TNext Then<TChild, TNext>(this IEnumerable<TChild> collection, Func<TChild, TNext> path) where TChild : ISqlEntity => throw NotRun();

    private static InvalidOperationException NotRun() => new("Then marks a step of an Include path: the path is read, never run.");
}
