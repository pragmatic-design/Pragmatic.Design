using System.Net;
using Pragmatic.Result;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Error returned when a remote boundary invocation fails.
///     Wraps ProblemDetails deserialized from the HTTP response.
/// </summary>
public sealed record RemoteError : IError
{
    /// <inheritdoc />
    public required string Code { get; init; }

    /// <inheritdoc />
    /// <remarks>
    ///     Typed as <see cref="int"/> because it implements <see cref="IError.StatusCode"/> (the
    ///     framework-wide error contract is provider-agnostic and does not depend on
    ///     <see cref="System.Net.HttpStatusCode"/>). Use <see cref="Status"/> for the strongly-typed
    ///     enum view.
    /// </remarks>
    public required int StatusCode { get; init; }

    /// <summary>
    ///     Strongly-typed view of <see cref="StatusCode"/> for HTTP-aware consumers, so they need not
    ///     cast the raw <see cref="int"/> at every use site.
    /// </summary>
    public HttpStatusCode Status => (HttpStatusCode)StatusCode;

    /// <inheritdoc />
    public required string Title { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <summary>
    ///     The remote host that returned the error (for diagnostics).
    /// </summary>
    public string? RemoteHost { get; init; }

    /// <summary>
    ///     Correlation ID from the remote response (for distributed tracing).
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Server-side processing duration in milliseconds (for performance diagnostics).
    /// </summary>
    public long? RemoteDurationMs { get; init; }
}
