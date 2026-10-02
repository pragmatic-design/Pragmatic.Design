using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     A specification that always returns true.
///     Singleton instance to avoid unnecessary allocations.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
internal sealed class TrueSpecification<T> : Specification<T>
{
    /// <summary>
    ///     The singleton instance.
    /// </summary>
    public static readonly TrueSpecification<T> Instance = new();

    private static readonly Expression<Func<T, bool>> TrueExpression = _ => true;

    private TrueSpecification()
    {
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        return TrueExpression;
    }

    public override string ToString() => "True";
}