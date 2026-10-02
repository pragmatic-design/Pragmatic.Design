using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Extension methods for filtering entities by hierarchical subtree using CTE.
///     Used for data-level authorization patterns (e.g., org unit hierarchy, category trees).
/// </summary>
public static class HierarchyQueryExtensions
{
    /// <summary>
    ///     Soft threshold above which the materialized subtree is considered large.
    ///     Materializing more IDs than this produces an oversized inlined <c>IN (...)</c> clause;
    ///     see remarks on <see cref="ResolveSubtreeIds{TKey}"/> for the design limitation.
    /// </summary>
    private const int LargeSubtreeThreshold = 2000;

    /// <summary>
    ///     AOT-safe cache for <c>HashSet&lt;TKey&gt;.Contains(TKey)</c> MethodInfo,
    ///     extracted via a typed delegate expression — zero runtime <c>GetMethod</c> calls.
    /// </summary>
    private static class ContainsMethodCache<TKey>
    {
        internal static readonly MethodInfo Method;

        static ContainsMethodCache()
        {
            // Extract MethodInfo from a typed delegate — no runtime reflection on the method name
            Expression<Func<HashSet<TKey>, TKey, bool>> stub = (set, key) => set.Contains(key);
            Method = ((MethodCallExpression)stub.Body).Method;
        }
    }

    /// <param name="query">The source queryable.</param>
    /// <typeparam name="TEntity">The entity type to filter.</typeparam>
    extension<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        /// <summary>
        ///     Filters entities to those belonging to a subtree rooted at <paramref name="rootId"/>.
        ///     Uses a recursive CTE to resolve all descendants of the root node.
        /// </summary>
        /// <typeparam name="TKey">The key type of the hierarchy node.</typeparam>
        /// <param name="dbContext">The DbContext for raw SQL execution.</param>
        /// <param name="rootId">The root node ID to start the subtree from.</param>
        /// <param name="entityNodeSelector">Expression to get the node ID from the entity.</param>
        /// <param name="tableName">The table name of the hierarchy node.</param>
        /// <param name="idColumn">The ID column name in the hierarchy table. Default: "Id".</param>
        /// <param name="parentIdColumn">The parent ID column name. Default: "ParentId".</param>
        /// <returns>Filtered queryable containing only entities in the subtree.</returns>
        /// <remarks>
        ///     The subtree IDs are resolved eagerly and inlined into the generated query as an
        ///     <c>IN (...)</c> clause. For very large subtrees this is inefficient — see the remarks on
        ///     <see cref="ResolveSubtreeIds{TKey}"/>.
        /// </remarks>
        /// <example>
        ///     <code>
        /// var employees = repository.Query()
        ///     .WhereInSubtree(
        ///         dbContext,
        ///         rootId: userOrgUnitId,
        ///         entityNodeSelector: e => e.OrgUnitId,
        ///         tableName: "OrgUnit");
        /// </code>
        /// </example>
        public IQueryable<TEntity> WhereInSubtree<TKey>(DbContext dbContext,
            TKey rootId,
            Expression<Func<TEntity, TKey>> entityNodeSelector,
            string tableName,
            string idColumn = "Id",
            string parentIdColumn = "ParentId") where TKey : notnull
        {
            // Resolve subtree IDs via recursive CTE
            var subtreeIds = ResolveSubtreeIds(dbContext, rootId, tableName, idColumn, parentIdColumn);

            return ApplyContains(query, entityNodeSelector, subtreeIds);
        }

        /// <summary>
        ///     Asynchronous counterpart of <see cref="WhereInSubtree{TEntity,TKey}"/>.
        ///     Resolves the subtree IDs without blocking the calling thread, then returns the filtered queryable.
        /// </summary>
        public async Task<IQueryable<TEntity>> WhereInSubtreeAsync<TKey>(DbContext dbContext,
            TKey rootId,
            Expression<Func<TEntity, TKey>> entityNodeSelector,
            string tableName,
            string idColumn = "Id",
            string parentIdColumn = "ParentId",
            CancellationToken cancellationToken = default) where TKey : notnull
        {
            var subtreeIds = await ResolveSubtreeIdsAsync(dbContext, rootId, tableName, idColumn, parentIdColumn, cancellationToken)
                .ConfigureAwait(false);

            return ApplyContains(query, entityNodeSelector, subtreeIds);
        }
    }

    private static IQueryable<TEntity> ApplyContains<TEntity, TKey>(
        IQueryable<TEntity> query,
        Expression<Func<TEntity, TKey>> entityNodeSelector,
        HashSet<TKey> subtreeIds)
        where TEntity : class
        where TKey : notnull
    {
        // Build the Where predicate: e => subtreeIds.Contains(e.NodeId)
        var parameter = entityNodeSelector.Parameters[0];
        var nodeIdAccess = entityNodeSelector.Body;
        var containsCall = Expression.Call(Expression.Constant(subtreeIds), ContainsMethodCache<TKey>.Method, nodeIdAccess);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(containsCall, parameter);

        return query.Where(predicate);
    }

    /// <summary>
    ///     Resolves all node IDs in a subtree using a recursive CTE.
    ///     The result is materialized into a <see cref="HashSet{T}"/> for in-memory Contains checks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Design limitation:</b> all subtree IDs are materialized into memory and the
    ///         downstream EF predicate inlines them as <c>IN (&#160;id1, id2, … &#160;)</c>.
    ///         For deep/wide hierarchies this produces an oversized SQL statement and degrades
    ///         performance. A correlated-subquery design (keeping the CTE inside the outer query)
    ///         would avoid the inlining but is out of scope for the provider-agnostic, AOT-safe
    ///         raw-SQL approach used here. When the materialized set exceeds
    ///         <c>2000</c> entries an OpenTelemetry event is recorded to flag the hot path.
    ///     </para>
    ///     <para>
    ///         <b>Note:</b> this overload runs synchronously and blocks the calling thread on the
    ///         CTE query. Prefer <see cref="ResolveSubtreeIdsAsync{TKey}"/> on async call sites.
    ///     </para>
    /// </remarks>
    public static HashSet<TKey> ResolveSubtreeIds<TKey>(
        DbContext dbContext,
        TKey rootId,
        string tableName,
        string idColumn = "Id",
        string parentIdColumn = "ParentId")
        where TKey : notnull
    {
        var sql = BuildSubtreeSql(dbContext, tableName, idColumn, parentIdColumn);

        var ids = dbContext.Database
            .SqlQueryRaw<TKey>(sql, rootId)
            .ToHashSet();

        WarnIfLarge(ids.Count, tableName);
        return ids;
    }

    /// <summary>
    ///     Asynchronous, non-blocking counterpart of <see cref="ResolveSubtreeIds{TKey}"/>.
    /// </summary>
    public static async Task<HashSet<TKey>> ResolveSubtreeIdsAsync<TKey>(
        DbContext dbContext,
        TKey rootId,
        string tableName,
        string idColumn = "Id",
        string parentIdColumn = "ParentId",
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        var sql = BuildSubtreeSql(dbContext, tableName, idColumn, parentIdColumn);

        var ids = await dbContext.Database
            .SqlQueryRaw<TKey>(sql, rootId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        WarnIfLarge(ids.Count, tableName);
        return ids;
    }

    /// <summary>
    ///     Builds the parameterized recursive-CTE SQL for descendant id resolution. The root id is
    ///     passed as a bound parameter (<c>{0}</c>); the dialect (RECURSIVE keyword + identifier
    ///     delimiters) is selected per provider. The id column is projected as
    ///     <c>"Value"</c> for <c>SqlQueryRaw&lt;TKey&gt;</c> scalar materialization.
    /// </summary>
    private static string BuildSubtreeSql(DbContext dbContext, string tableName, string idColumn, string parentIdColumn)
        => HierarchyCteSql.Build(
            dbContext.Database,
            tableName,
            idColumn,
            parentIdColumn,
            HierarchyCteSql.Direction.Descendants,
            selectColumns: idColumn,
            valueAlias: "Value"); // SqlQueryRaw<TKey> materializes the scalar column named "Value".

    private static void WarnIfLarge(int count, string tableName)
    {
        if (count <= LargeSubtreeThreshold)
            return;

        // The set is inlined as IN (...) downstream; flag the oversized statement for observability.
        Activity.Current?.AddEvent(new ActivityEvent(
            "pragmatic.hierarchy.large_subtree",
            tags: new ActivityTagsCollection
            {
                ["pragmatic.hierarchy.table"] = tableName,
                ["pragmatic.hierarchy.subtree_id_count"] = count,
                ["pragmatic.hierarchy.threshold"] = LargeSubtreeThreshold,
            }));
    }
}
