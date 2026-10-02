using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Helper for hierarchy-driven query filtering.
///     Filters entities that belong to the subtree of a given node
///     using a subquery join pattern (translates to SQL IN-clause).
/// </summary>
/// <remarks>
///     <para>
///         Use in <see cref="IQueryFilter{T}" /> implementations to restrict
///         data access based on organizational hierarchies (offices, departments,
///         categories, regions, etc.).
///     </para>
///     <para>
///         For CTE-based recursive queries on the hierarchy itself,
///         use <c>[GenerateHierarchy]</c> on the hierarchy entity which generates
///         <c>GetDescendants()</c> and <c>GetAncestors()</c> extension methods.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public class InvoiceOfficeFilter(ICurrentUser user, AppDbContext db) : IQueryFilter&lt;Invoice&gt;
///     {
///         public IQueryable&lt;Invoice&gt; Apply(IQueryable&lt;Invoice&gt; query)
///         {
///             if (user.Authorization.HasPermission("invoices.all"))
///                 return query;
///
///             var userOfficeId = user.GetClaim("office_id");
///             // Full subtree via the provider-aware recursive CTE extension.
///             return query.WhereInSubtree(
///                 db,
///                 rootId: userOfficeId!,
///                 entityNodeSelector: i =&gt; i.OfficeId,
///                 tableName: "Office");
///         }
///     }
///     </code>
/// </example>
public static class HierarchyFilter
{
    /// <summary>
    ///     Filters entities whose node reference is the root node itself or one of its
    ///     <em>direct</em> children (a single level of descent).
    ///     Uses a subquery join: the hierarchy is self-joined on parent = root,
    ///     then the entity query is filtered to include only matching nodes.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to filter.</typeparam>
    /// <typeparam name="TNode">The hierarchy node type (e.g., Office, Department).</typeparam>
    /// <typeparam name="TKey">The node key type (e.g., Guid, string, int).</typeparam>
    /// <param name="query">The entity query to filter.</param>
    /// <param name="hierarchy">The hierarchy table (e.g., dbContext.Offices).</param>
    /// <param name="nodeId">Expression to get the node's ID.</param>
    /// <param name="parentId">Expression to get the node's parent ID (null for root).</param>
    /// <param name="entityNodeKey">Expression to get the entity's node reference (FK).</param>
    /// <param name="rootNodeId">The root node ID — the root and its direct children are included.</param>
    /// <returns>Filtered query containing only entities on the root node or its direct children.</returns>
    /// <remarks>
    ///     <para>
    ///         This method matches the root node plus exactly one level of children
    ///         (a self-join on <c>parent == root</c>); it does <strong>not</strong> traverse the
    ///         full subtree. For deep hierarchies (3+ levels), use <c>[GenerateHierarchy]</c> on
    ///         the node entity for CTE-based recursive queries, then combine with
    ///         <see cref="WhereNodeIn{TEntity,TKey}" />.
    ///     </para>
    ///     <para>
    ///         Performance: ensure an index exists on the parent ID column and on the
    ///         entity's node FK column.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     <para>
    ///         <paramref name="parentId" /> is <c>TKey?</c> because a root has no parent, and that is
    ///         what forces <c>TKey : struct</c>: under <c>notnull</c> the compiler reads <c>TKey?</c>
    ///         on a value type as plain <c>TKey</c>, so the selector would demand a non-null parent id
    ///         that no tree can supply, and the method would be uncallable for <c>Guid</c> and
    ///         <c>int</c> keys — every real hierarchy.
    ///     </para>
    /// </remarks>
    public static IQueryable<TEntity> WhereInDirectChildren<TEntity, TNode, TKey>(
        IQueryable<TEntity> query,
        IQueryable<TNode> hierarchy,
        Expression<Func<TNode, TKey>> nodeId,
        Expression<Func<TNode, TKey?>> parentId,
        Expression<Func<TEntity, TKey>> entityNodeKey,
        TKey rootNodeId)
        where TEntity : class
        where TNode : class
        where TKey : struct
    {
        // Build the set of node IDs to match: root node + nodes whose parent is root.
        // This is one level of descent only. For multi-level traversal use CTE via [GenerateHierarchy].
        var directNodes = hierarchy
            .Where(BuildEqualityPredicate(nodeId, rootNodeId))
            .Concat(hierarchy.Where(BuildEqualityPredicate(parentId!, rootNodeId)));

        // Select just the IDs from the matched nodes
        var nodeIds = directNodes.Select(nodeId);

        // Filter entities where their node key is in the matched IDs
        return query.Where(BuildContainsPredicate(entityNodeKey, nodeIds));
    }

    /// <summary>
    ///     Filters entities where the node key is contained in a pre-computed set of node IDs.
    ///     Use this with <c>GetDescendants()</c> from <c>[GenerateHierarchy]</c> for deep hierarchies.
    /// </summary>
    /// <example>
    ///     <code>
    ///     var subtreeIds = dbContext.Offices.GetDescendants(rootId).Select(o => o.Id);
    ///     return HierarchyFilter.WhereNodeIn(query, i => i.OfficeId, subtreeIds);
    ///     </code>
    /// </example>
    public static IQueryable<TEntity> WhereNodeIn<TEntity, TKey>(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, TKey>> entityNodeKey,
        IQueryable<TKey> allowedNodeIds)
        where TEntity : class
    {
        return query.Where(BuildContainsPredicate(entityNodeKey, allowedNodeIds));
    }

    private static Expression<Func<T, bool>> BuildEqualityPredicate<T, TKey>(
        Expression<Func<T, TKey>> selector, TKey value)
    {
        var parameter = selector.Parameters[0];
        var body = Expression.Equal(selector.Body, Expression.Constant(value, typeof(TKey)));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression<Func<T, bool>> BuildContainsPredicate<T, TKey>(
        Expression<Func<T, TKey>> selector, IQueryable<TKey> values)
    {
        // Build: entity => values.Contains(entity.NodeKey)
        // Use Expression.Call with the typed Queryable.Contains<TKey> method
        var parameter = selector.Parameters[0];
        Expression<Func<bool>> containsStub = () => Queryable.Contains(values, default(TKey)!);
        var containsMethod = ((MethodCallExpression)containsStub.Body).Method;
        var body = Expression.Call(containsMethod, Expression.Constant(values), selector.Body);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
