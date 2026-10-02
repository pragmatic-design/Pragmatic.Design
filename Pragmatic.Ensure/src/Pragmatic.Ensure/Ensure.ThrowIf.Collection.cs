using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for parameter validation.
///     This partial contains collection checks.
/// </summary>
public static partial class Ensure
{
    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the collection is empty.
    ///     Does NOT check for null — use <see cref="ThrowIfNull{T}(T?, string?)" /> separately if needed,
    ///     or use <see cref="ThrowIfNullOrEmpty{T}(IEnumerable{T}?, string?)" /> for a combined check.
    /// </summary>
    /// <remarks>
    ///     A single <see cref="IEnumerable{T}" /> overload serves every collection type. It uses
    ///     <see cref="System.Linq.Enumerable.TryGetNonEnumeratedCount{TSource}(IEnumerable{TSource}, out int)" />,
    ///     which is O(1) for <see cref="ICollection{T}" />, <see cref="IReadOnlyCollection{T}" />, and arrays —
    ///     so no per-interface overload is needed, and passing a concrete <c>List&lt;T&gt;</c>/<c>HashSet&lt;T&gt;</c>
    ///     is never ambiguous.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfEmpty<T>(
        IEnumerable<T> collection,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        // Use TryGetNonEnumeratedCount to avoid consuming lazy sequences.
        // Falls back to a single MoveNext() when count is unavailable.
        if (System.Linq.Enumerable.TryGetNonEnumeratedCount(collection, out var count))
        {
            if (count == 0)
                throw new ArgumentException("Collection cannot be empty.", paramName);
        }
        else
        {
            using var enumerator = collection.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection cannot be empty.", paramName);
        }
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the array is empty.
    ///     Optimized overload for arrays (more specific than the <see cref="IEnumerable{T}" /> overload).
    ///     Does NOT check for null — use <see cref="ThrowIfNull{T}(T?, string?)" /> separately if needed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfEmpty<T>(
        T[] array,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
    {
        if (array.Length == 0)
            throw new ArgumentException("Array cannot be empty.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the collection is null.
    ///     Throws <see cref="ArgumentException" /> if the collection is empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNullOrEmpty<T>(
        [NotNull] IEnumerable<T>? collection,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        if (collection is null)
            throw new ArgumentNullException(paramName);
        // Use TryGetNonEnumeratedCount to avoid consuming lazy sequences.
        if (System.Linq.Enumerable.TryGetNonEnumeratedCount(collection, out var count))
        {
            if (count == 0)
                throw new ArgumentException("Collection cannot be empty.", paramName);
        }
        else
        {
            using var enumerator = collection.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new ArgumentException("Collection cannot be empty.", paramName);
        }
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the array is null.
    ///     Throws <see cref="ArgumentException" /> if the array is empty.
    ///     Optimized overload for arrays (more specific than the <see cref="IEnumerable{T}" /> overload).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNullOrEmpty<T>(
        [NotNull] T[]? array,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
    {
        if (array is null)
            throw new ArgumentNullException(paramName);
        if (array.Length == 0)
            throw new ArgumentException("Array cannot be empty.", paramName);
    }

    // =========================================================================
    // Contains Duplicate
    // =========================================================================

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the collection contains duplicate elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfContainsDuplicate<T>(
        IEnumerable<T> collection,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        var set = new HashSet<T>();
        foreach (var item in collection)
            if (!set.Add(item))
                throw new ArgumentException($"Collection contains duplicate element: {item}", paramName);
    }

    // =========================================================================
    // Contains Null
    // =========================================================================

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the collection is null.
    ///     Throws <see cref="ArgumentException" /> if any element in the collection is null.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfContainsNull<T>(
        [NotNull] IEnumerable<T?>? collection,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
        where T : class
    {
        if (collection is null)
            throw new ArgumentNullException(paramName);
        foreach (var item in collection)
        {
            if (item is null)
                throw new ArgumentException("Collection must not contain null elements.", paramName);
        }
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the array is null.
    ///     Throws <see cref="ArgumentException" /> if any element in the array is null.
    ///     Optimized overload for arrays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfContainsNull<T>(
        [NotNull] T?[]? array,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
        where T : class
    {
        if (array is null)
            throw new ArgumentNullException(paramName);
        foreach (var item in array)
        {
            if (item is null)
                throw new ArgumentException("Array must not contain null elements.", paramName);
        }
    }

    // =========================================================================
    // Collection Size Guards
    // =========================================================================

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the collection is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the collection count exceeds the maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountGreaterThan<T>(
        [NotNull] IReadOnlyCollection<T>? collection,
        int maxCount,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        if (collection is null)
            throw new ArgumentNullException(paramName);
        if (collection.Count > maxCount)
            throw new ArgumentOutOfRangeException(paramName, collection.Count,
                $"Collection count must not exceed {maxCount}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the collection is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the collection count is below the minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountLessThan<T>(
        [NotNull] IReadOnlyCollection<T>? collection,
        int minCount,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        if (collection is null)
            throw new ArgumentNullException(paramName);
        if (collection.Count < minCount)
            throw new ArgumentOutOfRangeException(paramName, collection.Count,
                $"Collection count must be at least {minCount}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the array is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the array length exceeds the maximum.
    ///     Optimized overload for arrays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountGreaterThan<T>(
        [NotNull] T[]? array,
        int maxCount,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
    {
        if (array is null)
            throw new ArgumentNullException(paramName);
        if (array.Length > maxCount)
            throw new ArgumentOutOfRangeException(paramName, array.Length,
                $"Array length must not exceed {maxCount}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the array is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the array length is below the minimum.
    ///     Optimized overload for arrays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountLessThan<T>(
        [NotNull] T[]? array,
        int minCount,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
    {
        if (array is null)
            throw new ArgumentNullException(paramName);
        if (array.Length < minCount)
            throw new ArgumentOutOfRangeException(paramName, array.Length,
                $"Array length must be at least {minCount}.");
    }

    // =========================================================================
    // Collection Count Range Guards
    // =========================================================================

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the collection is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the collection count is outside the specified range.
    ///     Convenience method combining <see cref="ThrowIfCountLessThan{T}(IReadOnlyCollection{T}?, int, string?)" />
    ///     and <see cref="ThrowIfCountGreaterThan{T}(IReadOnlyCollection{T}?, int, string?)" />.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="collection">The collection to check.</param>
    /// <param name="minCount">The minimum allowed count (inclusive).</param>
    /// <param name="maxCount">The maximum allowed count (inclusive).</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when collection is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is outside the range.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountOutOfRange<T>(
        [NotNull] IReadOnlyCollection<T>? collection,
        int minCount,
        int maxCount,
        [CallerArgumentExpression(nameof(collection))]
        string? paramName = null)
    {
        if (collection is null)
            throw new ArgumentNullException(paramName);
        if (collection.Count < minCount || collection.Count > maxCount)
            throw new ArgumentOutOfRangeException(paramName, collection.Count,
                $"Collection count must be between {minCount} and {maxCount}.");
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if the array is null.
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if the array length is outside the specified range.
    ///     Optimized overload for arrays.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="array">The array to check.</param>
    /// <param name="minCount">The minimum allowed length (inclusive).</param>
    /// <param name="maxCount">The maximum allowed length (inclusive).</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <exception cref="ArgumentNullException">Thrown when array is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when length is outside the range.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfCountOutOfRange<T>(
        [NotNull] T[]? array,
        int minCount,
        int maxCount,
        [CallerArgumentExpression(nameof(array))]
        string? paramName = null)
    {
        if (array is null)
            throw new ArgumentNullException(paramName);
        if (array.Length < minCount || array.Length > maxCount)
            throw new ArgumentOutOfRangeException(paramName, array.Length,
                $"Array length must be between {minCount} and {maxCount}.");
    }
}