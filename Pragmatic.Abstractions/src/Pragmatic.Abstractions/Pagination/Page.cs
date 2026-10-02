namespace Pragmatic.Pagination;

/// <summary>
///     A page of items with its position: a plain page, never a failure.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
/// <remarks>
///     <para>
///         For the result of a paged query — success or failure, with an error — see
///         <c>Pragmatic.Persistence.Query.Results.PagedResult&lt;T&gt;</c>, which is a different thing.
///         This type was called <c>PagedResult&lt;T&gt;</c> too, one namespace apart, and a file importing
///         both got CS0104.
///     </para>
///     <para>
///         The page number is <see cref="Number" />, not <c>Page</c>: C# refuses a member named after its
///         type (CS0542).
///     </para>
/// </remarks>
public sealed class Page<T>
{
    /// <summary>
    ///     Initializes a new instance of <see cref="Page{T}" />.
    /// </summary>
    /// <param name="items">The items in the page.</param>
    /// <param name="totalCount">The total number of items across all pages. Must be &gt;= 0.</param>
    /// <param name="number">The page number (1-based). Must be &gt;= 1.</param>
    /// <param name="pageSize">The number of items per page. Must be &gt;= 0.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="number"/> &lt; 1, <paramref name="pageSize"/> &lt; 0,
    ///     or <paramref name="totalCount"/> &lt; 0.
    /// </exception>
    public Page(IReadOnlyList<T> items, int totalCount, int number, int pageSize)
    {
        // Guarded here rather than left to deferred failure: a null Items surfaces as a
        // NullReferenceException at first enumeration, far from the construction site.
        ArgumentNullException.ThrowIfNull(items);

        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number), number,
                "Page number must be at least 1.");
        if (pageSize < 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize,
                "Page size must not be negative.");
        if (totalCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount,
                "Total count must not be negative.");

        Items = items;
        TotalCount = totalCount;
        Number = number;
        PageSize = pageSize;
    }

    /// <summary>
    ///     The items in the page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    ///     The total number of items across all pages.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    ///     The page number (1-based).
    /// </summary>
    public int Number { get; }

    /// <summary>
    ///     The number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    ///     The total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalCount / (double)PageSize)
        : 0;

    /// <summary>
    ///     Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => Number > 1;

    /// <summary>
    ///     Whether there is a next page.
    /// </summary>
    public bool HasNextPage => Number < TotalPages;
}
