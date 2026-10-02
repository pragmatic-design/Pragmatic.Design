using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     Helper to replace parameter expressions in expression trees.
///     This is necessary for proper AND/OR composition that EF Core can translate.
/// </summary>
internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _newParameter;
    private readonly ParameterExpression _oldParameter;

    private ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        _oldParameter = oldParameter;
        _newParameter = newParameter;
    }

    /// <summary>
    ///     Replaces all occurrences of oldParameter with newParameter in the expression.
    /// </summary>
    public static Expression Replace(Expression expression, ParameterExpression oldParameter,
        ParameterExpression newParameter)
    {
        return new ParameterReplacer(oldParameter, newParameter).Visit(expression);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _oldParameter ? _newParameter : base.VisitParameter(node);
    }
}