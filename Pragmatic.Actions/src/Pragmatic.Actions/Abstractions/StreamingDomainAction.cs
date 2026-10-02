using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     A domain action that streams items instead of returning a single result.
///     Exposed over HTTP as a Server-Sent Events endpoint when combined with [Endpoint].
/// </summary>
/// <remarks>
///     <para>
///         By deriving from <see cref="DomainAction{TReturn}" /> with the stream itself as the
///         return value, the ENTIRE existing invoker pipeline (validation, authorization,
///         logging filters) runs unchanged and — crucially — BEFORE the stream is consumed:
///         a short-circuiting filter fails the invocation before the response starts.
///     </para>
///     <para>
///         Streaming actions are read-oriented: the pipeline's post-execution steps
///         (SaveChanges, [Raises]) run when the stream is HANDED OVER, not when it finishes.
///         Writes performed while enumerating are not part of the action's unit of work.
///     </para>
/// </remarks>
/// <typeparam name="TItem">The stream item type.</typeparam>
public abstract class StreamingDomainAction<TItem>
    : DomainAction<IAsyncEnumerable<Result<TItem, IError>>>
{
    /// <summary>Produces the stream. Yield a failure to terminate the stream with an error.</summary>
    /// <param name="ct">Cancellation token (request aborted).</param>
    public abstract IAsyncEnumerable<Result<TItem, IError>> ExecuteStream(CancellationToken ct = default);

    /// <summary>
    ///     Sealed: hands the stream to the pipeline without consuming it, so filters run
    ///     pre-stream and the enumeration happens in the endpoint writer.
    /// </summary>
    public sealed override Task<Result<IAsyncEnumerable<Result<TItem, IError>>, IError>> Execute(
        CancellationToken ct = default)
        => Task.FromResult(
            Result<IAsyncEnumerable<Result<TItem, IError>>, IError>.Success(ExecuteStream(ct)));
}
