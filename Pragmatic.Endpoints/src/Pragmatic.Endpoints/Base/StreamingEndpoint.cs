using Pragmatic.Result;

namespace Pragmatic.Endpoints.Base;

/// <summary>
///     Base class for Server-Sent Events endpoints streaming <typeparamref name="TItem" />.
/// </summary>
/// <remarks>
///     <para>
///         The generated handler peeks the first element: a failure yielded FIRST becomes a
///         normal HTTP error response (ProblemDetails with the error's status code) — use it
///         for not-found/precondition checks. Once an item has been emitted the response is
///         already 200; later failures are sent as a terminal SSE <c>event: error</c>.
///     </para>
///     <para>
///         Backpressure is pull-based: the next item is requested only after the previous one
///         has been flushed to the client. The <c>ct</c> parameter is the request-aborted
///         token — enumeration stops when the client disconnects.
///     </para>
/// </remarks>
/// <typeparam name="TItem">The stream item type.</typeparam>
public abstract class StreamingEndpoint<TItem>
{
    /// <summary>Produces the stream. Yield a failure to terminate with an error.</summary>
    /// <param name="ct">Cancellation token (request aborted).</param>
    public abstract IAsyncEnumerable<Result<TItem>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for SSE streaming endpoints with one typed error.
///     See <see cref="StreamingEndpoint{TItem}" /> for streaming semantics.
/// </summary>
/// <typeparam name="TItem">The stream item type.</typeparam>
/// <typeparam name="TError1">The first error type.</typeparam>
public abstract class StreamingEndpoint<TItem, TError1>
    where TError1 : IError
{
    /// <summary>Produces the stream. Yield a failure to terminate with an error.</summary>
    /// <param name="ct">Cancellation token (request aborted).</param>
    public abstract IAsyncEnumerable<Result<TItem, TError1>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for SSE streaming endpoints with two typed errors.
///     See <see cref="StreamingEndpoint{TItem}" /> for streaming semantics.
/// </summary>
/// <typeparam name="TItem">The stream item type.</typeparam>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
public abstract class StreamingEndpoint<TItem, TError1, TError2>
    where TError1 : IError
    where TError2 : IError
{
    /// <summary>Produces the stream. Yield a failure to terminate with an error.</summary>
    /// <param name="ct">Cancellation token (request aborted).</param>
    public abstract IAsyncEnumerable<Result<TItem, TError1, TError2>> HandleAsync(CancellationToken ct = default);
}

/// <summary>
///     Base class for SSE streaming endpoints with three typed errors.
///     See <see cref="StreamingEndpoint{TItem}" /> for streaming semantics.
/// </summary>
/// <typeparam name="TItem">The stream item type.</typeparam>
/// <typeparam name="TError1">The first error type.</typeparam>
/// <typeparam name="TError2">The second error type.</typeparam>
/// <typeparam name="TError3">The third error type.</typeparam>
public abstract class StreamingEndpoint<TItem, TError1, TError2, TError3>
    where TError1 : IError
    where TError2 : IError
    where TError3 : IError
{
    /// <summary>Produces the stream. Yield a failure to terminate with an error.</summary>
    /// <param name="ct">Cancellation token (request aborted).</param>
    public abstract IAsyncEnumerable<Result<TItem, TError1, TError2, TError3>> HandleAsync(
        CancellationToken ct = default);
}
