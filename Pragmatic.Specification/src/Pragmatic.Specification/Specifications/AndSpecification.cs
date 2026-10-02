using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     Represents a logical AND combination of two specifications.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
internal sealed class AndSpecification<T> : Specification<T>
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;
    private readonly Lazy<Expression<Func<T, bool>>> _cachedExpression;

    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        Ensure.Ensure.ThrowIfNull(left);
        Ensure.Ensure.ThrowIfNull(right);
        _left = left;
        _right = right;
        _cachedExpression = new Lazy<Expression<Func<T, bool>>>(BuildExpression);
    }

    public override Expression<Func<T, bool>> ToExpression() => _cachedExpression.Value;

    public override string ToString() => $"({_left} AND {_right})";

    private Expression<Func<T, bool>> BuildExpression()
    {
        var leftExpr = _left.ToExpression();
        var rightExpr = _right.ToExpression();

        // Use a single parameter for both expressions
        var parameter = Expression.Parameter(typeof(T), "x");

        // Replace parameters in both expressions with our unified parameter
        var leftBody = ParameterReplacer.Replace(leftExpr.Body, leftExpr.Parameters[0], parameter);
        var rightBody = ParameterReplacer.Replace(rightExpr.Body, rightExpr.Parameters[0], parameter);

        // Combine with AndAlso (&&)
        var body = Expression.AndAlso(leftBody, rightBody);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}