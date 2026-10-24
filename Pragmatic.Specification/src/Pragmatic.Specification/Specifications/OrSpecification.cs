using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     Represents a logical OR combination of two specifications.
/// </summary>
/// <remarks>
///     Public so an engine that translates a composed specification itself, the Sql engine, can walk its structure
///     and compose the leaves it compiled along it. Built only by <c>|</c> or <c>Or</c>.
/// </remarks>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class OrSpecification<T> : Specification<T>
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;
    private readonly Lazy<Expression<Func<T, bool>>> _cachedExpression;

    internal OrSpecification(Specification<T> left, Specification<T> right)
    {
        Ensure.Ensure.ThrowIfNull(left);
        Ensure.Ensure.ThrowIfNull(right);
        _left = left;
        _right = right;
        _cachedExpression = new Lazy<Expression<Func<T, bool>>>(BuildExpression);
    }

    /// <summary>The left operand.</summary>
    public Specification<T> Left => _left;

    /// <summary>The right operand.</summary>
    public Specification<T> Right => _right;

    public override Expression<Func<T, bool>> ToExpression() => _cachedExpression.Value;

    public override string ToString() => $"({_left} OR {_right})";

    private Expression<Func<T, bool>> BuildExpression()
    {
        var leftExpr = _left.ToExpression();
        var rightExpr = _right.ToExpression();

        // Use a single parameter for both expressions
        var parameter = Expression.Parameter(typeof(T), "x");

        // Replace parameters in both expressions with our unified parameter
        var leftBody = ParameterReplacer.Replace(leftExpr.Body, leftExpr.Parameters[0], parameter);
        var rightBody = ParameterReplacer.Replace(rightExpr.Body, rightExpr.Parameters[0], parameter);

        // Combine with OrElse (||)
        var body = Expression.OrElse(leftBody, rightBody);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}