using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     HTTP-based invoker for remote VoidDomainAction invocations.
///     Serializes the action as JSON, POSTs to <c>/_pragmatic/invoke</c>,
///     and deserializes the response back to <see cref="VoidResult{IError}" />.
/// </summary>
/// <typeparam name="TAction">The void action type.</typeparam>
public class HttpVoidActionInvoker<TAction>(
    IHttpClientFactory httpClientFactory,
    string httpClientName) : IVoidDomainActionInvoker<TAction>
    where TAction : IVoidExecutable
{
    private static readonly string ActionTypeName = typeof(TAction).FullName!;

    /// <remarks>
    ///     The resolver comes from the shared seam: what crosses a boundary must be a type the
    ///     generated contexts cover, or a published AOT binary cannot serialize it.
    /// </remarks>
    private static readonly global::System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = global::Pragmatic.Serialization.PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };

    /// <inheritdoc />
    public async Task<VoidResult<IError>> InvokeAsync(TAction action, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(httpClientName);

        // A missing base URL (unconfigured remote boundary) would otherwise surface as an
        // InvalidOperationException on the relative POST — return a Result instead (REMOTE-NOBASEURL).
        if (client.BaseAddress is null)
            return VoidResult<IError>.Failure(new RemoteError
            {
                Code = "REMOTE_NO_BASE_URL",
                StatusCode = 503,
                Title = "Remote boundary base URL not configured",
                Description = $"No base URL is configured for HttpClient '{httpClientName}'. Set 'Pragmatic:RemoteBoundaries:{{Module}}:BaseUrl'."
            });

        var request = new PragmaticInvokeRequest(ActionTypeName, JsonSerializer.SerializeToElement(action, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TAction>(global::Pragmatic.Serialization.PragmaticRemotePayload.Options)));

        try
        {
            using var response = await client.PostAsJsonAsync("/_pragmatic/invoke", request, global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<PragmaticInvokeRequest>(SerializerOptions), ct).ConfigureAwait(false);

            var invokeResponse = await response.Content
                .ReadFromJsonAsync(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<PragmaticInvokeResponse>(SerializerOptions), ct)
                .ConfigureAwait(false);

            if (invokeResponse is null)
            {
                return VoidResult<IError>.Failure(new RemoteError
                {
                    Code = "REMOTE_INVOKE_FAILED",
                    StatusCode = (int)response.StatusCode,
                    Title = "Remote invocation failed",
                    Description = "Could not deserialize response from remote boundary.",
                    RemoteHost = client.BaseAddress?.ToString()
                });
            }

            if (invokeResponse.IsSuccess)
                return VoidResult<IError>.Success();

            return VoidResult<IError>.Failure(ToRemoteError(invokeResponse, client.BaseAddress));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-initiated cancellation must surface as cancellation, not a remote failure.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or JsonException or NotSupportedException)
        {
            // A boundary that is down, times out, or returns a non-JSON body must become a Result —
            // never escape as an exception — honoring Result-over-exceptions on the remote path (REMOTE-THROW).
            return VoidResult<IError>.Failure(new RemoteError
            {
                Code = "REMOTE_TRANSPORT_FAILED",
                StatusCode = 503,
                Title = "Remote invocation failed",
                Description = ex.Message,
                RemoteHost = client.BaseAddress?.ToString()
            });
        }
    }

    private static RemoteError ToRemoteError(PragmaticInvokeResponse response, Uri? remoteHost)
    {
        var problem = response.Error;
        if (problem is null)
        {
            return new RemoteError
            {
                Code = "REMOTE_UNKNOWN_ERROR",
                StatusCode = 500,
                Title = "Unknown remote error",
                RemoteHost = remoteHost?.ToString(),
                CorrelationId = response.CorrelationId,
                RemoteDurationMs = response.DurationMs
            };
        }

        var code = "REMOTE_ERROR";
        if (problem.Extensions.TryGetValue("code", out var codeObj) && codeObj is JsonElement codeElement)
            code = codeElement.GetString() ?? code;

        return new RemoteError
        {
            Code = code,
            StatusCode = problem.Status ?? 500,
            Title = problem.Title ?? "Remote error",
            Description = problem.Detail,
            RemoteHost = remoteHost?.ToString(),
            CorrelationId = response.CorrelationId,
            RemoteDurationMs = response.DurationMs
        };
    }
}
