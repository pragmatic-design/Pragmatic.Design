using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     Represents a logical NOT of a specification.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
internal sealed class NotSpecification<T> : Specification<T>
{
    private readonly Specification<T> _inner;
    private readonly Lazy<Expression<Func<T, bool>>> _cachedExpression;

    public NotSpecification(Specification<T> specification)
    {
        Ensure.Ensure.ThrowIfNull(specification);
        _inner = specification;
        _cachedExpression = new Lazy<Expression<Func<T, bool>>>(BuildExpression);
    }

    public override Expression<Func<T, bool>> ToExpression() => _cachedExpression.Value;

    public override string ToString() => $"(NOT {_inner})";

    private Expression<Func<T, bool>> BuildExpression()
    {
        var expr = _inner.ToExpression();
        var parameter = Expression.Parameter(typeof(T), "x");

        // Replace parameter with our unified parameter
        var body = ParameterReplacer.Replace(expr.Body, expr.Parameters[0], parameter);

        // Negate with Not (!)
        var negatedBody = Expression.Not(body);

        return Expression.Lambda<Func<T, bool>>(negatedBody, parameter);
    }
}