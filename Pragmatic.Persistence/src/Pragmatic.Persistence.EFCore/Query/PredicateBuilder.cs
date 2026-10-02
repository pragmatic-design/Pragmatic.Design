using System.Linq.Expressions;

namespace Pragmatic.Persistence.EFCore.Query;

/// <summary>
///     Combines LINQ predicate expressions with AND/OR logic.
///     Used by source-generated FilterDto code to build composite Where clauses.
/// </summary>
public static class PredicateBuilder
{
    /// <summary>
    ///     Combines two predicates with logical OR.
    /// </summary>
    public static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var param = left.Parameters[0];
        var body = Expression.OrElse(
            left.Body,
            new ParameterReplacer(right.Parameters[0], param).Visit(right.Body));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    ///     Combines two predicates with logical AND.
    /// </summary>
    public static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var param = left.Parameters[0];
        var body = Expression.AndAlso(
            left.Body,
            new ParameterReplacer(right.Parameters[0], param).Visit(right.Body));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : node;
    }
}
