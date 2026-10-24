using System.Linq.Expressions;

namespace Pragmatic.Specification.Specifications;

/// <summary>
///     A specification that always returns true.
///     Singleton instance to avoid unnecessary allocations.
/// </summary>
/// <remarks>
///     Public so an engine that translates a composed specification itself, the Sql engine, can walk its structure
///     and compose the leaves it compiled along it. Built only by <c>Spec&lt;T&gt;.True</c>.
/// </remarks>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class TrueSpecification<T> : Specification<T>
{
    /// <summary>
    ///     The singleton instance.
    /// </summary>
    internal static readonly TrueSpecification<T> Instance = new();

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