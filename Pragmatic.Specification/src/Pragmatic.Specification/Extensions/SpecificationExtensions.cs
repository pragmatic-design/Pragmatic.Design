using System.Runtime.CompilerServices;

// The namespace of the type these extend, not the folder they sit in: a `using Pragmatic.Specification`
// that brings in Specification<T> has to bring in the methods that make it usable. One namespace below
// it, they were invisible — IntelliSense cannot offer a method whose namespace is not imported, so an
// API that exists is indistinguishable from one that does not. Measured on a consumer: four files
// importing Pragmatic.Specification, zero importing the extensions, and every use of a specification
// against a queryable written through .ToExpression() by hand.

// ReSharper disable once CheckNamespace
namespace Pragmatic.Specification;

/// <summary>
///     Extension methods for applying specifications to collections.
/// </summary>
public static class SpecificationExtensions
{
    /// <summary>
    ///     Filters an IQueryable using the specification's expression.
    ///     The expression is translated to the underlying query provider (e.g., SQL for EF Core).
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to filter.</param>
    /// <param name="specification">The specification to apply.</param>
    /// <returns>A filtered queryable.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueryable<T> Where<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.Where(specification.ToExpression());
    }

    /// <param name="enumerable">The enumerable to filter.</param>
    /// <typeparam name="T">The entity type.</typeparam>
    extension<T>(IEnumerable<T> enumerable)
    {
        /// <summary>
        ///     Filters an IEnumerable using the specification's compiled predicate.
        ///     Uses the cached compiled delegate for optimal performance.
        /// </summary>
        /// <param name="specification">The specification to apply.</param>
        /// <returns>A filtered enumerable.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<T> Where(ISpecification<T> specification)
        {
            return enumerable.Where(specification.IsSatisfiedBy);
        }

        /// <summary>
        ///     Determines whether any element satisfies the specification.
        /// </summary>
        /// <param name="specification">The specification to evaluate.</param>
        /// <returns>True if any element satisfies the specification.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Any(ISpecification<T> specification)
        {
            return enumerable.Any(specification.IsSatisfiedBy);
        }
    }

    /// <summary>
    ///     Determines whether any element in the queryable satisfies the specification.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to check.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>True if any element satisfies the specification.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Any<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.Any(specification.ToExpression());
    }

    /// <summary>
    ///     Determines whether all elements satisfy the specification.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="enumerable">The enumerable to check.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>True if all elements satisfy the specification.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool All<T>(
        this IEnumerable<T> enumerable,
        ISpecification<T> specification)
    {
        return enumerable.All(specification.IsSatisfiedBy);
    }

    /// <summary>
    ///     Determines whether all elements in the queryable satisfy the specification.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to check.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>True if all elements satisfy the specification.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool All<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.All(specification.ToExpression());
    }

    /// <summary>
    ///     Returns the first element that satisfies the specification, or default if none found.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="enumerable">The enumerable to search.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The first matching element, or default.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? FirstOrDefault<T>(
        this IEnumerable<T> enumerable,
        ISpecification<T> specification)
    {
        return enumerable.FirstOrDefault(specification.IsSatisfiedBy);
    }

    /// <summary>
    ///     Returns the first element that satisfies the specification, or default if none found.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to search.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The first matching element, or default.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? FirstOrDefault<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.FirstOrDefault(specification.ToExpression());
    }

    /// <summary>
    ///     Returns the single element that satisfies the specification, or default if none found.
    ///     Throws if more than one element matches.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="enumerable">The enumerable to search.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The single matching element, or default.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? SingleOrDefault<T>(
        this IEnumerable<T> enumerable,
        ISpecification<T> specification)
    {
        return enumerable.SingleOrDefault(specification.IsSatisfiedBy);
    }

    /// <summary>
    ///     Returns the single element that satisfies the specification, or default if none found.
    ///     Throws if more than one element matches.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to search.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The single matching element, or default.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? SingleOrDefault<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.SingleOrDefault(specification.ToExpression());
    }

    /// <summary>
    ///     Counts elements that satisfy the specification.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="enumerable">The enumerable to count.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The count of matching elements.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<T>(
        this IEnumerable<T> enumerable,
        ISpecification<T> specification)
    {
        return enumerable.Count(specification.IsSatisfiedBy);
    }

    /// <summary>
    ///     Counts elements that satisfy the specification.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to count.</param>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>The count of matching elements.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count<T>(
        this IQueryable<T> queryable,
        ISpecification<T> specification)
    {
        return queryable.Count(specification.ToExpression());
    }
}