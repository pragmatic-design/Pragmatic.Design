using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     A specification that always returns false.
///     Singleton instance to avoid unnecessary allocations.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
internal sealed class FalseSpecification<T> : Specification<T>
{
    /// <summary>
    ///     The singleton instance.
    /// </summary>
    public static readonly FalseSpecification<T> Instance = new();

    private static readonly Expression<Func<T, bool>> FalseExpression = _ => false;

    private FalseSpecification()
    {
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        return FalseExpression;
    }

    public override string ToString() => "False";
}