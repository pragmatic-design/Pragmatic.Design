using System.Text.Json.Serialization;
using Pragmatic.Result;

namespace Pragmatic.Persistence.Query.Results;

/// <summary>
///     Represents a paged query result that IS a Result type.
///     Contains either a successful page of items with metadata, or an error.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
/// <typeparam name="TError">The error type (must implement IError).</typeparam>
/// <remarks>
///     <para>
///         PagedResult follows the Result pattern - it's either successful with items,
///         or failed with an error. Use Match() for exhaustive handling.
///     </para>
///     <para>
///         For common scenarios, use <see cref="PagedResult{T}"/> which uses <see cref="QueryError"/>
///         as the default error type.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Create success
/// var result = PagedResult&lt;Order, QueryError&gt;.Success(orders, totalCount: 100, page: 1, pageSize: 20);
///
/// // Create failure
/// var error = PagedResult&lt;Order, QueryError&gt;.Failure(new QueryError.Database { Message = "Connection failed" });
///
/// // Pattern matching
/// var response = result.Match(
///     success: (items, total) => Ok(new { items, total }),
///     failure: error => BadRequest(error.Message));
/// </code>
/// </example>
public sealed class PagedResult<T, TError> where TError : IError
{
    private readonly IReadOnlyList<T>? _items;
    private readonly TError? _error;

    private PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        _items = items;
        _error = default;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    private PagedResult(TError error)
    {
        _items = null;
        _error = error;
        TotalCount = 0;
        Page = 0;
        PageSize = 0;
    }

    /// <summary>Deserialization constructor for HybridCache / JSON roundtrips.</summary>
    [JsonConstructor]
    internal PagedResult(IReadOnlyList<T>? items, TError? error, int totalCount, int page, int pageSize)
    {
        _items = items;
        _error = error;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    ///     Whether the result represents a successful query.
    /// </summary>
    public bool IsSuccess => _error is null;

    /// <summary>
    ///     Whether the result represents a failed query.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    ///     The items for the current page.
    ///     Returns an empty list when <see cref="IsFailure"/> is true (safe for serialization).
    /// </summary>
    public IReadOnlyList<T> Items => _items ?? [];

    /// <summary>
    ///     The error, or <c>default</c> when <see cref="IsSuccess"/> is true (safe for serialization).
    /// </summary>
    public TError? Error => _error;

    /// <summary>
    ///     The total count of items across all pages.
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    ///     The current page number (1-based).
    /// </summary>
    public int Page { get; }

    /// <summary>
    ///     The number of items per page.
    /// </summary>
    public int PageSize { get; }

    /// <summary>
    ///     The total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    /// <summary>
    ///     Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    ///     Whether there is a next page.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    ///     The number of items to skip (for offset-based pagination).
    /// </summary>
    public int Skip => Page > 0 && PageSize > 0 ? (Page - 1) * PageSize : 0;

    /// <summary>
    ///     Creates a successful paged result.
    /// </summary>
    /// <param name="items">The items for this page.</param>
    /// <param name="totalCount">The total count across all pages.</param>
    /// <param name="page">The current page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>A successful paged result.</returns>
    public static PagedResult<T, TError> Success(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        => new(items, totalCount, page, pageSize);

    /// <summary>
    ///     Creates a failed paged result.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed paged result.</returns>
    public static PagedResult<T, TError> Failure(TError error)
        => new(error);

    /// <summary>
    ///     Creates an empty successful result.
    /// </summary>
    /// <param name="page">The page number (default 1).</param>
    /// <param name="pageSize">The page size (default 20).</param>
    /// <returns>An empty paged result.</returns>
    public static PagedResult<T, TError> Empty(int page = 1, int pageSize = 20)
        => new([], 0, page, pageSize);

    /// <summary>
    ///     Pattern matches on the result, calling the appropriate function.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="success">Function to call on success with items and total count.</param>
    /// <param name="failure">Function to call on failure with the error.</param>
    /// <returns>The result of the matched function.</returns>
    public TResult Match<TResult>(
        Func<IReadOnlyList<T>, int, TResult> success,
        Func<TError, TResult> failure)
        => IsSuccess ? success(_items!, TotalCount) : failure(_error!);

    /// <summary>
    ///     Pattern matches on the result with full paging metadata.
    /// </summary>
    /// <typeparam name="TResult">The return type.</typeparam>
    /// <param name="success">Function with items, total, page, and pageSize.</param>
    /// <param name="failure">Function to call on failure with the error.</param>
    /// <returns>The result of the matched function.</returns>
    public TResult Match<TResult>(
        Func<IReadOnlyList<T>, int, int, int, TResult> success,
        Func<TError, TResult> failure)
        => IsSuccess ? success(_items!, TotalCount, Page, PageSize) : failure(_error!);

    /// <summary>
    ///     Executes an action if the result is successful.
    /// </summary>
    /// <param name="action">The action to execute with items.</param>
    /// <returns>This result for chaining.</returns>
    public PagedResult<T, TError> OnSuccess(Action<IReadOnlyList<T>> action)
    {
        if (IsSuccess)
            action(_items!);
        return this;
    }

    /// <summary>
    ///     Executes an action if the result is a failure.
    /// </summary>
    /// <param name="action">The action to execute with the error.</param>
    /// <returns>This result for chaining.</returns>
    public PagedResult<T, TError> OnFailure(Action<TError> action)
    {
        if (IsFailure)
            action(_error!);
        return this;
    }

    /// <summary>
    ///     Projects the items to a different type.
    /// </summary>
    /// <typeparam name="TResult">The target item type.</typeparam>
    /// <param name="selector">The projection function.</param>
    /// <returns>A new paged result with projected items, or the same error.</returns>
    public PagedResult<TResult, TError> Select<TResult>(Func<T, TResult> selector)
        => IsSuccess
            ? PagedResult<TResult, TError>.Success(
                _items!.Select(selector).ToList(),
                TotalCount,
                Page,
                PageSize)
            : PagedResult<TResult, TError>.Failure(_error!);

    /// <summary>
    ///     Converts this result to use a different error type.
    /// </summary>
    /// <typeparam name="TOtherError">The new error type.</typeparam>
    /// <param name="errorMapper">Function to convert the error.</param>
    /// <returns>A new paged result with the mapped error type.</returns>
    public PagedResult<T, TOtherError> MapError<TOtherError>(Func<TError, TOtherError> errorMapper)
        where TOtherError : IError
        => IsSuccess
            ? PagedResult<T, TOtherError>.Success(_items!, TotalCount, Page, PageSize)
            : PagedResult<T, TOtherError>.Failure(errorMapper(_error!));
}

/// <summary>
///     Paged result with the default <see cref="QueryError"/> error type.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
/// <remarks>
///     Convenience alias for <see cref="PagedResult{T,TError}"/> with <see cref="QueryError"/>.
/// </remarks>
public sealed class PagedResult<T>
{
    private readonly PagedResult<T, QueryError> _inner;

    private PagedResult(PagedResult<T, QueryError> inner) => _inner = inner;

    /// <summary>Deserialization constructor for HybridCache / JSON roundtrips.</summary>
    [JsonConstructor]
    internal PagedResult(IReadOnlyList<T>? items, QueryError? error, int totalCount, int page, int pageSize)
    {
        _inner = error is not null
            ? PagedResult<T, QueryError>.Failure(error)
            : PagedResult<T, QueryError>.Success(items ?? [], totalCount, page, pageSize);
    }

    /// <inheritdoc cref="PagedResult{T,TError}.IsSuccess"/>
    public bool IsSuccess => _inner.IsSuccess;

    /// <inheritdoc cref="PagedResult{T,TError}.IsFailure"/>
    public bool IsFailure => _inner.IsFailure;

    /// <inheritdoc cref="PagedResult{T,TError}.Items"/>
    public IReadOnlyList<T> Items => _inner.Items;

    /// <inheritdoc cref="PagedResult{T,TError}.Error"/>
    public QueryError? Error => _inner.Error;

    /// <inheritdoc cref="PagedResult{T,TError}.TotalCount"/>
    public int TotalCount => _inner.TotalCount;

    /// <inheritdoc cref="PagedResult{T,TError}.Page"/>
    public int Page => _inner.Page;

    /// <inheritdoc cref="PagedResult{T,TError}.PageSize"/>
    public int PageSize => _inner.PageSize;

    /// <inheritdoc cref="PagedResult{T,TError}.TotalPages"/>
    public int TotalPages => _inner.TotalPages;

    /// <inheritdoc cref="PagedResult{T,TError}.HasPreviousPage"/>
    public bool HasPreviousPage => _inner.HasPreviousPage;

    /// <inheritdoc cref="PagedResult{T,TError}.HasNextPage"/>
    public bool HasNextPage => _inner.HasNextPage;

    /// <inheritdoc cref="PagedResult{T,TError}.Skip"/>
    public int Skip => _inner.Skip;

    /// <inheritdoc cref="PagedResult{T,TError}.Success"/>
    public static PagedResult<T> Success(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        => new(PagedResult<T, QueryError>.Success(items, totalCount, page, pageSize));

    /// <inheritdoc cref="PagedResult{T,TError}.Failure"/>
    public static PagedResult<T> Failure(QueryError error)
        => new(PagedResult<T, QueryError>.Failure(error));

    /// <inheritdoc cref="PagedResult{T,TError}.Empty"/>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 20)
        => new(PagedResult<T, QueryError>.Empty(page, pageSize));

    /// <inheritdoc cref="PagedResult{T,TError}.Match{TResult}(Func{IReadOnlyList{T},int,TResult},Func{TError,TResult})"/>
    public TResult Match<TResult>(
        Func<IReadOnlyList<T>, int, TResult> success,
        Func<QueryError, TResult> failure)
        => _inner.Match(success, failure);

    /// <inheritdoc cref="PagedResult{T,TError}.Match{TResult}(Func{IReadOnlyList{T},int,int,int,TResult},Func{TError,TResult})"/>
    public TResult Match<TResult>(
        Func<IReadOnlyList<T>, int, int, int, TResult> success,
        Func<QueryError, TResult> failure)
        => _inner.Match(success, failure);

    /// <inheritdoc cref="PagedResult{T,TError}.OnSuccess"/>
    public PagedResult<T> OnSuccess(Action<IReadOnlyList<T>> action)
    {
        _inner.OnSuccess(action);
        return this;
    }

    /// <inheritdoc cref="PagedResult{T,TError}.OnFailure"/>
    public PagedResult<T> OnFailure(Action<QueryError> action)
    {
        _inner.OnFailure(action);
        return this;
    }

    /// <inheritdoc cref="PagedResult{T,TError}.Select{TResult}"/>
    public PagedResult<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        var innerResult = _inner.Select(selector);
        return innerResult.IsSuccess
            ? PagedResult<TResult>.Success(innerResult.Items, innerResult.TotalCount, innerResult.Page, innerResult.PageSize)
            : PagedResult<TResult>.Failure(innerResult.Error!);
    }

    /// <summary>
    ///     Converts to the generic error type version.
    /// </summary>
    /// <returns>The inner PagedResult with explicit error type.</returns>
    public PagedResult<T, QueryError> AsGeneric() => _inner;

    /// <summary>
    ///     Creates from a generic PagedResult.
    /// </summary>
    public static PagedResult<T> FromGeneric(PagedResult<T, QueryError> generic) => new(generic);
}
