using System.Linq.Expressions;
using Pragmatic.Specification.Specifications;

namespace Pragmatic.Specification;

/// <summary>
///     Factory for creating specifications.
/// </summary>
/// <typeparam name="T">The entity type the specifications will apply to.</typeparam>
public static class Spec<T>
{
    /// <summary>
    ///     Returns a specification that is always satisfied (returns true for any entity).
    ///     Useful as a starting point for conditional composition.
    /// </summary>
    /// <example>
    ///     <code>
    /// var spec = Spec&lt;User&gt;.True;
    /// if (filterActive) spec = spec.And(activeSpec);
    /// if (filterRole) spec = spec.And(roleSpec);
    /// </code>
    /// </example>
    public static Specification<T> True => TrueSpecification<T>.Instance;

    /// <summary>
    ///     Returns a specification that is never satisfied (returns false for any entity).
    /// </summary>
    public static Specification<T> False => FalseSpecification<T>.Instance;

    /// <summary>
    ///     Creates a specification from a predicate expression.
    /// </summary>
    /// <param name="predicate">The predicate expression defining the specification.</param>
    /// <returns>A new specification wrapping the predicate.</returns>
    /// <example>
    ///     <code>
    /// var activeUsers = Spec&lt;User&gt;.Where(u => u.IsActive);
    /// var admins = Spec&lt;User&gt;.Where(u => u.Role == "Admin");
    /// </code>
    /// </example>
    public static Specification<T> Where(Expression<Func<T, bool>> predicate)
    {
        return new ExpressionSpecification<T>(predicate);
    }
}