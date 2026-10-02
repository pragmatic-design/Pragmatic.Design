using System.Linq.Expressions;

namespace Pragmatic.Specification;

/// <summary>
///     Defines the specification pattern contract for composable, reusable predicates.
///     Specifications can be used with both IQueryable (for database queries)
///     and IEnumerable (for in-memory filtering).
/// </summary>
/// <typeparam name="T">The entity type this specification applies to.</typeparam>
public interface ISpecification<T>
{
    /// <summary>
    ///     Returns the expression tree representing this specification's predicate.
    ///     Used for query translation (e.g., EF Core to SQL).
    /// </summary>
    /// <returns>An expression that can be used in LINQ queries.</returns>
    Expression<Func<T, bool>> ToExpression();

    /// <summary>
    ///     Evaluates whether the given entity satisfies this specification.
    ///     Uses a compiled delegate for optimal in-memory performance.
    /// </summary>
    /// <param name="entity">The entity to evaluate.</param>
    /// <returns>True if the entity satisfies the specification; otherwise, false.</returns>
    bool IsSatisfiedBy(T entity);
}
