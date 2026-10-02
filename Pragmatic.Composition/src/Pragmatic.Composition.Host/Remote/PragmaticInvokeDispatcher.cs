using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pragmatic.Result;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Static helper used by the generated <c>/_pragmatic/invoke</c> endpoint.
///     Provides methods for dispatching actions via DI-resolved invokers
///     and serializing the results.
/// </summary>
public static class PragmaticInvokeDispatcher
{
    /// <remarks>
    ///     The resolver comes from the shared seam, so the payloads crossing a boundary are the types
    ///     the generated contexts cover. A fresh options instance resolves by reflection, which is fine
    ///     until the host is published Native AOT.
    /// </remarks>
    private static readonly global::System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase,
        TypeInfoResolver = global::Pragmatic.Serialization.PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };

    /// <summary>
    ///     Invokes a DomainAction via its <c>IDomainActionInvoker</c> and returns a serialized response.
    /// </summary>
    public static async Task<IResult> InvokeActionAsync<TAction, TReturn>(
        TAction action,
        Pragmatic.Actions.Invoker.IDomainActionInvoker<TAction, TReturn> invoker,
        CancellationToken ct)
        where TAction : Pragmatic.Actions.Abstractions.DomainAction<TReturn>
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await invoker.InvokeAsync(action, ct).ConfigureAwait(false);
        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        return result.Match(
            success => Results.Ok(new PragmaticInvokeResponse(true, JsonSerializer.SerializeToElement(success, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TReturn>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options)), null)
                { StatusCode = 200, CorrelationId = correlationId, DurationMs = durationMs }),
            error => Results.Ok(new PragmaticInvokeResponse(false, null, ToProblemDetails(error))
                { StatusCode = error.StatusCode, CorrelationId = correlationId, DurationMs = durationMs }));
    }

    /// <summary>
    ///     Invokes an action whose success value is a file and streams the bytes back instead of the
    ///     JSON envelope <see cref="InvokeActionAsync{TAction,TReturn}" /> would produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A file cannot travel inside <see cref="PragmaticInvokeResponse.Value" />: serializing the
    ///         success value would emit the record's shape (including an unreadable <c>Stream</c>) and
    ///         leave the bytes behind. The body is therefore the file itself.
    ///     </para>
    ///     <para>
    ///         That costs the envelope its ability to carry the outcome, so <b>failure is signalled by the
    ///         HTTP status code</b> — the envelope is still the body, but with a 4xx/5xx status the client
    ///         can branch on before touching the stream. A file whose own content type is
    ///         <c>application/json</c> would otherwise be indistinguishable from an error envelope.
    ///     </para>
    /// </remarks>
    /// <param name="action">The action to invoke.</param>
    /// <param name="invoker">The DI-resolved invoker.</param>
    /// <param name="toFileResult">
    ///     Converts the success value into a file result. Supplied by the generated dispatch table, which
    ///     knows the concrete file type — this assembly deliberately does not reference Pragmatic.Endpoints.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IResult> InvokeFileActionAsync<TAction, TReturn>(
        TAction action,
        Pragmatic.Actions.Invoker.IDomainActionInvoker<TAction, TReturn> invoker,
        Func<TReturn, IResult> toFileResult,
        CancellationToken ct)
        where TAction : Pragmatic.Actions.Abstractions.DomainAction<TReturn>
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await invoker.InvokeAsync(action, ct).ConfigureAwait(false);
        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        return result.Match(
            toFileResult,
            error => Results.Json(
                new PragmaticInvokeResponse(false, null, ToProblemDetails(error))
                {
                    StatusCode = error.StatusCode,
                    CorrelationId = correlationId,
                    DurationMs = durationMs
                },
                global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<PragmaticInvokeResponse>(SerializerOptions),
                statusCode: ToHttpStatusCode(error.StatusCode)));
    }

    /// <summary>
    ///     Keeps the response status inside the error range. An <see cref="IError" /> may carry 0 (never
    ///     set) or a non-HTTP code; emitting it verbatim would make a failure look like a success to the
    ///     status-code check the file path relies on.
    /// </summary>
    private static int ToHttpStatusCode(int errorStatusCode)
        => errorStatusCode is >= 400 and <= 599 ? errorStatusCode : 500;

    /// <summary>
    ///     Invokes a VoidDomainAction via its <c>IVoidDomainActionInvoker</c> and returns a serialized response.
    /// </summary>
    public static async Task<IResult> InvokeVoidActionAsync<TAction>(
        TAction action,
        Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<TAction> invoker,
        CancellationToken ct)
        where TAction : Pragmatic.Actions.Abstractions.IVoidExecutable
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var startTimestamp = Stopwatch.GetTimestamp();

        var result = await invoker.InvokeAsync(action, ct).ConfigureAwait(false);
        var durationMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        return result.Match(
            () => Results.Ok(new PragmaticInvokeResponse(true, null, null)
                { StatusCode = 200, CorrelationId = correlationId, DurationMs = durationMs }),
            error => Results.Ok(new PragmaticInvokeResponse(false, null, ToProblemDetails(error))
                { StatusCode = error.StatusCode, CorrelationId = correlationId, DurationMs = durationMs }));
    }

    /// <summary>
    ///     Maps an <see cref="IError"/> to ProblemDetails for the remote-invoke response.
    /// </summary>
    /// <remarks>
    ///     <b>Trust boundary:</b> the <c>/_pragmatic/invoke</c> endpoint is internal boundary-to-boundary
    ///     RPC within one trust domain — callers are other Pragmatic hosts configured with explicit
    ///     <c>Pragmatic:RemoteBoundaries:{Module}:BaseUrl</c> entries, NOT arbitrary public clients.
    ///     <see cref="IError.Description"/> is therefore forwarded in <see cref="ProblemDetails.Detail"/>
    ///     by design, so the calling boundary can surface the real remote error. If this endpoint is ever
    ///     exposed to an untrusted network, gate <c>Detail</c> behind an environment/redaction check before
    ///     emitting it, the same way the maintenance endpoint gates stack traces.
    /// </remarks>
    private static ProblemDetails ToProblemDetails(IError error)
    {
        var pd = new ProblemDetails
        {
            Status = error.StatusCode,
            Title = string.IsNullOrEmpty(error.Title) ? "Error" : error.Title,
            Detail = error.Description,
            Type = $"https://httpstatuses.io/{error.StatusCode}"
        };

        pd.Extensions["code"] = error.Code;

        return pd;
    }
}
