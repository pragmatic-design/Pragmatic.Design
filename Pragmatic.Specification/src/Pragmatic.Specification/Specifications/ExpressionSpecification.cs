using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     A specification that wraps a predicate expression.
///     This is the primary way to create specifications from lambda expressions.
/// </summary>
/// <typeparam name="T">The entity type this specification applies to.</typeparam>
internal sealed class ExpressionSpecification<T> : Specification<T>
{
    private readonly Expression<Func<T, bool>> _expression;

    /// <summary>
    ///     Creates a new specification from the given predicate expression.
    /// </summary>
    /// <param name="expression">The predicate expression.</param>
    /// <exception cref="ArgumentNullException">Thrown when expression is null.</exception>
    public ExpressionSpecification(Expression<Func<T, bool>> expression)
    {
        Ensure.Ensure.ThrowIfNull(expression);
        _expression = expression;
    }

    /// <inheritdoc />
    public override Expression<Func<T, bool>> ToExpression()
    {
        return _expression;
    }

    /// <summary>
    ///     Returns the expression body as a string for debugging.
    /// </summary>
    public override string ToString() => _expression.Body.ToString();
}