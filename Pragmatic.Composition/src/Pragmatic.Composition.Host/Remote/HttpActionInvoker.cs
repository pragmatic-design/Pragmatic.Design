using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Result;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     HTTP-based invoker for remote DomainAction invocations.
///     Serializes the action as JSON, POSTs to <c>/_pragmatic/invoke</c>,
///     and deserializes the response back to <see cref="Result{TReturn, IError}" />.
/// </summary>
/// <typeparam name="TAction">The action type.</typeparam>
/// <typeparam name="TReturn">The return type.</typeparam>
public class HttpActionInvoker<TAction, TReturn>(
    IHttpClientFactory httpClientFactory,
    string httpClientName) : IDomainActionInvoker<TAction, TReturn>
    where TAction : DomainAction<TReturn>
{
    // Validate once: a null factory or a blank client name would otherwise surface as an NRE or,
    // worse, silently resolve the DEFAULT HttpClient (wrong base address) on the first invocation.
    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly string _httpClientName = string.IsNullOrWhiteSpace(httpClientName)
        ? throw new ArgumentException("Named HttpClient name must be non-empty so the correct remote base address is used.", nameof(httpClientName))
        : httpClientName;

    private static readonly string ActionTypeName = typeof(TAction).FullName!;

    /// <remarks>
    ///     The resolver is explicit. A bare <see cref="JsonSerializerOptions" /> only acquires the
    ///     reflection resolver when something serializes through it, so asking it for a
    ///     <c>JsonTypeInfo</c> first answers "no metadata" for every type — which is how two
    ///     Composition tests failed the moment these calls became typed.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = global::Pragmatic.Serialization.PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };

    /// <inheritdoc />
    public async Task<Result<TReturn, IError>> InvokeAsync(TAction action, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient(_httpClientName);

        // A missing base URL (unconfigured remote boundary) would otherwise surface as an
        // InvalidOperationException on the relative POST — return a Result instead (REMOTE-NOBASEURL).
        if (client.BaseAddress is null)
            return Result<TReturn, IError>.Failure(new RemoteError
            {
                Code = "REMOTE_NO_BASE_URL",
                StatusCode = 503,
                Title = "Remote boundary base URL not configured",
                Description = $"No base URL is configured for HttpClient '{_httpClientName}'. Set 'Pragmatic:RemoteBoundaries:{{Module}}:BaseUrl'."
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
                return Result<TReturn, IError>.Failure(new RemoteError
                {
                    Code = "REMOTE_INVOKE_FAILED",
                    StatusCode = (int)response.StatusCode,
                    Title = "Remote invocation failed",
                    Description = "Could not deserialize response from remote boundary.",
                    RemoteHost = client.BaseAddress?.ToString()
                });
            }

            if (invokeResponse is { IsSuccess: true, Value: not null })
            {
                var value = invokeResponse.Value.Value.Deserialize(global::Pragmatic.Serialization.PragmaticJsonTypeInfo.For<TReturn>(SerializerOptions));
                if (value is not null)
                    return Result<TReturn, IError>.Success(value);

                return Result<TReturn, IError>.Failure(new RemoteError
                {
                    Code = "REMOTE_DESERIALIZE_FAILED",
                    StatusCode = 500,
                    Title = "Remote deserialization failed",
                    Description = $"Could not deserialize return value of type {typeof(TReturn).Name}.",
                    RemoteHost = client.BaseAddress?.ToString()
                });
            }

            return Result<TReturn, IError>.Failure(ToRemoteError(invokeResponse, client.BaseAddress));
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
            return Result<TReturn, IError>.Failure(new RemoteError
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

        // Extract error code from ProblemDetails extensions
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
