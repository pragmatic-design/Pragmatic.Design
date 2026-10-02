using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Pragmatic.Persistence.EFCore.Query.Visitors;

/// <summary>
///     Helper methods for the <see cref="PragmaticQueryFilterVisitor"/>.
///     AOT-safe: OpenWhereMethod resolved via expression tree (no GetMethods scan).
///     All other results cached per type — MakeGenericMethod is AOT-safe on concrete types.
/// </summary>
internal static class VisitorExpressionHelpers
{
    private static readonly ConcurrentDictionary<Type, Type?> ElementTypeCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> WhereMethodCache = new();

    private static readonly ConcurrentDictionary<Type, MethodInfo> ToListMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> CountMethodCache = new();

    // Enumerable.Where<T> open generic — resolved via expression tree, zero GetMethods() reflection
    private static readonly MethodInfo OpenWhereMethod =
        ((MethodCallExpression)((Expression<Func<IEnumerable<object>, IEnumerable<object>>>)
            (x => x.Where(_ => true))).Body).Method.GetGenericMethodDefinition();

    // Enumerable.ToList<T> and Enumerable.Count<T>, resolved the same way.
    private static readonly MethodInfo OpenToListMethod =
        ((MethodCallExpression)((Expression<Func<IEnumerable<object>, List<object>>>)
            (x => x.ToList())).Body).Method.GetGenericMethodDefinition();

    private static readonly MethodInfo OpenCountMethod =
        ((MethodCallExpression)((Expression<Func<IEnumerable<object>, int>>)
            (x => x.Count())).Body).Method.GetGenericMethodDefinition();

    /// <summary><c>Enumerable.ToList(sequence)</c>, for a sequence of <paramref name="elementType" />.</summary>
    public static Expression ToListOf(Expression sequence, Type elementType)
        => Expression.Call(
            ToListMethodCache.GetOrAdd(elementType, static type => Close(OpenToListMethod, type)),
            sequence);

    /// <summary><c>Enumerable.Count(sequence)</c>, the element type read off the sequence.</summary>
    public static Expression CountOf(Expression sequence)
    {
        var elementType = GetCollectionElementType(sequence.Type)
                          ?? throw new InvalidOperationException($"'{sequence.Type}' is not a sequence.");

        return Expression.Call(
            CountMethodCache.GetOrAdd(elementType, static type => Close(OpenCountMethod, type)),
            sequence);
    }

    /// <summary>
    ///     Closes an open generic method over concrete types — the one place the visitor instantiates a
    ///     generic at runtime.
    /// </summary>
    /// <remarks>
    ///     One place rather than one per shape: every instantiation the visitor needs is of a method over the
    ///     entity types of the model, which is the case NativeAOT supports, and saying it once keeps that
    ///     statement — and the analyser's warning about it — in one spot.
    /// </remarks>
    public static MethodInfo Close(MethodInfo open, params Type[] typeArguments)
        => open.MakeGenericMethod(typeArguments);

    /// <summary>
    ///     Determines whether a member expression represents a collection navigation property.
    /// </summary>
    public static bool IsCollectionNavigation(MemberExpression node)
    {
        if (node.Member is not PropertyInfo)
            return false;

        var type = node.Type;
        if (type == typeof(string) || type == typeof(byte[]))
            return false;

        return type.IsGenericType && IsCollectionInterface(type);
    }

    /// <summary>
    ///     Gets the element type from a collection type. Cached per type.
    /// </summary>
    public static Type? GetCollectionElementType(Type collectionType)
    {
        return ElementTypeCache.GetOrAdd(collectionType, static type =>
        {
            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                if (IsKnownCollectionDefinition(genericDef))
                    return type.GetGenericArguments()[0];
            }

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }

            return null;
        });
    }

    /// <summary>
    ///     Wraps a member access expression with <c>Enumerable.Where(filter)</c>.
    ///     AOT-safe: MakeGenericMethod on concrete types is supported by NativeAOT.
    ///     Cached per element type — one call per type in the app lifetime.
    /// </summary>
    public static Expression WrapWithEnumerableWhere(MemberExpression member, LambdaExpression filter)
    {
        var elementType = GetCollectionElementType(member.Type);
        if (elementType is null)
            return member;

        var whereMethod = WhereMethodCache.GetOrAdd(elementType,
            static type => Close(OpenWhereMethod, type));

        return Expression.Call(whereMethod, member, filter);
    }

    /// <summary>
    ///     Checks whether an expression already has a <c>Where</c> clause with an equivalent filter applied.
    /// </summary>
    /// <remarks>
    ///     The comparison rebinds the existing filter's parameters onto the incoming filter's parameters
    ///     before comparing bodies, so lambdas that differ only in parameter naming
    ///     (<c>e => e.X</c> vs <c>x => x.X</c>) are recognised as equivalent. It remains a structural
    ///     string heuristic: it will not detect filters that are semantically equal yet written
    ///     differently (e.g. <c>a &amp;&amp; b</c> vs <c>b &amp;&amp; a</c>), which is acceptable here —
    ///     a missed match only causes a redundant (idempotent) <c>Where</c>, never an incorrect result.
    /// </remarks>
    public static bool IsAlreadyFiltered(Expression expression, LambdaExpression filter)
    {
        if (expression is not MethodCallExpression { Method.Name: "Where" } call)
            return false;

        if (call.Arguments is not [_, UnaryExpression { Operand: LambdaExpression existingFilter }])
            return false;

        if (existingFilter.Parameters.Count != filter.Parameters.Count)
            return false;

        // Rebind existing-filter parameters to the incoming filter's parameters so that only
        // structural differences (not parameter names) affect the comparison.
        var rebound = ParameterRebinder.Rebind(existingFilter, filter.Parameters);
        return rebound.Body.ToString() == filter.Body.ToString();
    }

    /// <summary>
    ///     Replaces the parameters of a lambda with a target set of parameters (positionally).
    /// </summary>
    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly IReadOnlyDictionary<ParameterExpression, ParameterExpression> _map;

        private ParameterRebinder(IReadOnlyDictionary<ParameterExpression, ParameterExpression> map) => _map = map;

        public static LambdaExpression Rebind(LambdaExpression source, IReadOnlyList<ParameterExpression> target)
        {
            var map = new Dictionary<ParameterExpression, ParameterExpression>(source.Parameters.Count);
            for (var i = 0; i < source.Parameters.Count; i++)
                map[source.Parameters[i]] = target[i];

            var rebinder = new ParameterRebinder(map);
            var body = rebinder.Visit(source.Body);
            return Expression.Lambda(body, target);
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => _map.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }

    private static bool IsCollectionInterface(Type type)
    {
        return IsKnownCollectionDefinition(type.GetGenericTypeDefinition());
    }

    private static bool IsKnownCollectionDefinition(Type genericDef)
    {
        return genericDef == typeof(ICollection<>)
               || genericDef == typeof(IList<>)
               || genericDef == typeof(List<>)
               || genericDef == typeof(IEnumerable<>)
               || genericDef == typeof(IReadOnlyCollection<>)
               || genericDef == typeof(IReadOnlyList<>)
               || genericDef == typeof(HashSet<>);
    }
}
