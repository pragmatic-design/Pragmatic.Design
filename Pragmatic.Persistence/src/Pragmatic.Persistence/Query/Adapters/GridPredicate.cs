using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Joins the predicates a grid's clauses produce, honouring the connector each clause declares.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b><see cref="FilterClause.Logic" /> has to be read.</b> Walking the clauses applying
///         <c>query = ApplyXFilter(query, filter)</c> composes in AND whatever the clause says — so
///         "the word is in the term <em>or</em> in its definition", the first thing any grid's search
///         box asks for, would come back as the intersection. No error, a shorter list, and a reader
///         who concludes the data is not there.
///     </para>
///     <para>
///         A <c>Where</c> already applied cannot be OR-ed with the next one, which is why the bridge
///         builds predicates and combines them here before a single <c>Where</c>.
///     </para>
/// </remarks>
public static class GridPredicate
{
    /// <summary>Both must hold.</summary>
    public static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.AndAlso);

    /// <summary>Either may hold.</summary>
    public static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.OrElse);

    /// <summary>
    ///     Joins two predicates under one parameter.
    /// </summary>
    /// <remarks>
    ///     The bodies are rebound onto a fresh parameter rather than joined with <c>Expression.Invoke</c>:
    ///     EF Core translates the first and refuses the second, and a predicate that only works in memory
    ///     would move the filtering off the database without saying so.
    /// </remarks>
    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> join)
    {
        var parameter = Expression.Parameter(typeof(T), "e");

        var body = join(
            new ParameterRebinder(left.Parameters[0], parameter).Visit(left.Body),
            new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
