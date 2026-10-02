using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.EFCore.Query.Visitors;

/// <summary>
///     Expression visitor that injects query filters into navigation property access.
///     Transforms collection navigations (e.g., <c>order.LineItems</c>) into filtered
///     versions (<c>order.LineItems.Where(e => !e.IsDeleted)</c>).
/// </summary>
/// <remarks>
///     <para>
///         AOT-full: uses pre-resolved MethodInfo from FilterMap (SG-generated).
///         No runtime reflection on the hot path.
///     </para>
///     <para>
///         ⚠️ <b>The filtered navigation is an <c>IEnumerable&lt;T&gt;</c>; the model declared an
///         <c>ICollection&lt;T&gt;</c> or a <c>List&lt;T&gt;</c>.</b> Handing the <c>Where</c> back as it is broke
///         every place that needed the declared type — an <c>Include</c> lambda threw, and so did a read of
///         <c>Count</c>. So the declared type is restored with <c>ToList()</c>, and then taken away again
///         wherever an <c>IEnumerable</c> is accepted: an argument of <c>Any</c>, <c>Count()</c>,
///         <c>Select</c>; the lambda of an <c>Include</c>, rebuilt as EF's filtered include; and
///         <c>Count</c>, read as <c>Enumerable.Count</c>. What is left carries the <c>ToList()</c>.
///     </para>
///     <para>
///         <b>Where a navigation is read decides which map filters it</b> (<see cref="FilterScope" />):
///         the operator of the query that reaches it — an <c>Include</c>, a projection, or any other
///         operator — sets the position, and everything nested in that operator's lambda keeps it.
///     </para>
/// </remarks>
public sealed class PragmaticQueryFilterVisitor : ExpressionVisitor
{
    private readonly FilterMap _included;
    private readonly FilterMap _predicated;
    private readonly FilterMap _projected;

    // The ToList calls this visitor introduced, so they can be recognised and removed where not needed.
    private readonly HashSet<Expression> _restored = new(ReferenceEqualityComparer.Instance);

    // The position of the query operator whose lambda is being visited; null outside any.
    private FilterScope? _position;

    /// <summary>One map for every position a collection can be read in.</summary>
    public PragmaticQueryFilterVisitor(FilterMap filterMap)
        : this(filterMap, filterMap, filterMap)
    {
    }

    /// <summary>A map per position: loaded with its entity, read in a predicate, read in a projection.</summary>
    /// <param name="included">For an <c>Include</c>: <see cref="FilterScope.Collections" />.</param>
    /// <param name="predicated">For every operator that is not a projection: <see cref="FilterScope.Subqueries" />.</param>
    /// <param name="projected">For a projection: <see cref="FilterScope.Projections" />.</param>
    public PragmaticQueryFilterVisitor(FilterMap included, FilterMap predicated, FilterMap projected)
    {
        _included = included;
        _predicated = predicated;
        _projected = projected;
    }

    /// <summary>
    ///     Applies navigation filters to the given query expression.
    ///     Returns the original expression unchanged if the FilterMap is empty.
    /// </summary>
    public Expression Apply(Expression expression)
    {
        if (!_included.HasFilters && !_predicated.HasFilters && !_projected.HasFilters)
            return expression;
        return Visit(expression);
    }

    /// <summary>The map for the position being visited.</summary>
    /// <remarks>
    ///     Outside any operator — an expression visited on its own, as a test does — a navigation is
    ///     taken as loaded with its entity.
    /// </remarks>
    private FilterMap Current => _position switch
    {
        FilterScope.Subqueries => _predicated,
        FilterScope.Projections => _projected,
        _ => _included
    };

    /// <summary>
    ///     Visits member access expressions. If the member is a collection navigation
    ///     and the element type has a filter in the FilterMap, wraps it with <c>.Where(filter)</c>.
    /// </summary>
    protected override Expression VisitMember(MemberExpression node)
    {
        var visited = (MemberExpression)base.VisitMember(node);

        // `c.Products.Count` on a filtered navigation counts what the filter lets through.
        if (visited.Expression is { } inner
            && _restored.Contains(inner)
            && visited.Member.Name == "Count"
            && visited.Type == typeof(int))
        {
            return VisitorExpressionHelpers.CountOf(Unrestore(inner));
        }

        if (!VisitorExpressionHelpers.IsCollectionNavigation(visited))
            return visited;

        var elementType = VisitorExpressionHelpers.GetCollectionElementType(visited.Type);
        if (elementType is null)
            return visited;

        var map = Current;
        if (!map.TryGetFilter(elementType, out var filter))
            return visited;

        if (VisitorExpressionHelpers.IsAlreadyFiltered(visited, filter!))
            return visited;

        // Primary: use SG pre-resolved MethodInfo from FilterMap (zero reflection)
        var filtered = map.TryGetWhereMethod(elementType, out var whereMethod) && whereMethod is not null
            ? Expression.Call(whereMethod, visited, filter!)
            // Fallback: cached reflection for types not known to the SG
            : VisitorExpressionHelpers.WrapWithEnumerableWhere(visited, filter!);

        return Restore(filtered, visited.Type, elementType);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var visited = _position is null && IsQueryOperator(node.Method)
            ? VisitOperator(node)
            : (MethodCallExpression)base.VisitMethodCall(node);

        if (IsInclude(visited.Method) && TryRebuildInclude(visited, out var include))
            return include;

        if (visited.Object is not null || !visited.Arguments.Any(_restored.Contains))
            return visited;

        // A static method that takes an IEnumerable does not need the list: pass it the filter itself,
        // which is the shape EF translates everywhere.
        var parameters = visited.Method.GetParameters();
        var arguments = new Expression[visited.Arguments.Count];
        var changed = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = visited.Arguments[i];
            if (_restored.Contains(argument) && parameters[i].ParameterType.IsAssignableFrom(Unrestore(argument).Type))
            {
                arguments[i] = Unrestore(argument);
                changed = true;
            }
            else
            {
                arguments[i] = argument;
            }
        }

        return changed ? Expression.Call(visited.Method, arguments) : visited;
    }

    /// <summary>
    ///     Gives the filtered sequence back the type the model declared, so the node that holds it still
    ///     type-checks: a list for any collection a list can be.
    /// </summary>
    /// <remarks>
    ///     A navigation of a type a list is not — a <c>HashSet&lt;T&gt;</c> — cannot carry the filter at
    ///     all. Refusing is the only honest answer: leaving it unfiltered would hand every caller the rows the
    ///     filter exists to withhold.
    /// </remarks>
    private Expression Restore(Expression filtered, Type declared, Type elementType)
    {
        if (declared.IsAssignableFrom(filtered.Type))
            return filtered;

        var list = VisitorExpressionHelpers.ToListOf(filtered, elementType);
        if (!declared.IsAssignableFrom(list.Type))
        {
            throw new InvalidOperationException(
                $"A navigation of type '{declared}' is guarded by a query filter, and a filtered '{elementType.Name}' "
                + "sequence cannot be given that type. Declare the navigation as ICollection<T>, IList<T>, List<T>, "
                + "IReadOnlyCollection<T>, IReadOnlyList<T> or IEnumerable<T>.");
        }

        _restored.Add(list);
        return list;
    }

    private static Expression Unrestore(Expression restored) => ((MethodCallExpression)restored).Arguments[0];

    /// <summary>
    ///     An operator of the query itself — <see cref="Queryable" />, or EF's <c>Include</c> family —
    ///     whose lambdas set the position of what they read.
    /// </summary>
    private static bool IsQueryOperator(MethodInfo method)
        => method.DeclaringType == typeof(Queryable)
           || method.DeclaringType == typeof(EntityFrameworkQueryableExtensions);

    /// <summary>
    ///     Visits a query operator's arguments, each lambda in the position the operator gives it.
    /// </summary>
    /// <remarks>
    ///     The source — the query the operator is applied to — is visited as it is, so the operators
    ///     before this one decide their own lambdas' positions.
    /// </remarks>
    private MethodCallExpression VisitOperator(MethodCallExpression node)
    {
        var instance = Visit(node.Object);
        var arguments = new Expression[node.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = node.Arguments[i];
            if (argument is not UnaryExpression { NodeType: ExpressionType.Quote } and not LambdaExpression)
            {
                arguments[i] = Visit(argument);
                continue;
            }

            _position = PositionOf(node.Method, i);
            try
            {
                arguments[i] = Visit(argument);
            }
            finally
            {
                _position = null;
            }
        }

        return node.Update(instance, arguments);
    }

    /// <summary>
    ///     Where the lambda at <paramref name="index" /> of <paramref name="method" /> reads what it reads.
    /// </summary>
    /// <remarks>
    ///     A join's key selectors match rows and its result selector shapes them, so the two sides of one
    ///     call take different positions.
    /// </remarks>
    private static FilterScope PositionOf(MethodInfo method, int index)
    {
        if (method.DeclaringType == typeof(EntityFrameworkQueryableExtensions))
            return FilterScope.Collections;

        return method.Name switch
        {
            nameof(Queryable.Select) or nameof(Queryable.SelectMany) or nameof(Queryable.GroupBy)
                => FilterScope.Projections,
            nameof(Queryable.Join) or nameof(Queryable.GroupJoin)
                => index == 4 ? FilterScope.Projections : FilterScope.Subqueries,
            _ => FilterScope.Subqueries
        };
    }

    private static bool IsInclude(MethodInfo method)
        => method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
           && method.Name is nameof(EntityFrameworkQueryableExtensions.Include)
               or nameof(EntityFrameworkQueryableExtensions.ThenInclude)
           && method.IsGenericMethod;

    /// <summary>
    ///     <c>Include(c => c.Products)</c> on a filtered navigation becomes EF's filtered include,
    ///     <c>Include(c => c.Products.Where(filter))</c>, whose property type is the sequence.
    /// </summary>
    private bool TryRebuildInclude(MethodCallExpression call, out Expression rebuilt)
    {
        rebuilt = call;
        if (call.Arguments.Count != 2
            || call.Arguments[1] is not UnaryExpression { Operand: LambdaExpression lambda }
            || !_restored.Contains(lambda.Body))
        {
            return false;
        }

        var body = Unrestore(lambda.Body);

        // The last generic argument is the included property; everything before it is unchanged.
        var generic = call.Method.GetGenericArguments();
        generic[^1] = body.Type;
        var method = VisitorExpressionHelpers.Close(call.Method.GetGenericMethodDefinition(), generic);

        // The delegate type is read off the closed method's own parameter, Expression<Func<…>>, rather
        // than built: building one is a second runtime instantiation.
        var delegateType = method.GetParameters()[1].ParameterType.GetGenericArguments()[0];
        var newLambda = Expression.Lambda(delegateType, body, lambda.Parameters);

        rebuilt = Expression.Call(method, call.Arguments[0], Expression.Quote(newLambda));
        return true;
    }
}
