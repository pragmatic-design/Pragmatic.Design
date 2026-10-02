using System.Text.Json;
using Pragmatic.Resilience.State;

namespace Pragmatic.Gateway.Resilience;

/// <summary>
/// YARP proxy pipeline middleware that applies circuit breaker + timeout per cluster.
/// Sits inside MapReverseProxy pipeline, wraps the actual forwarding call.
/// </summary>
internal sealed class ProxyResilienceMiddleware(
    ICircuitBreakerStateStore stateStore,
    GatewayResilienceOptions options,
    ILogger<ProxyResilienceMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!options.Enabled)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var clusterId = ResolveClusterId(context);
        await ExecuteWithResilienceAsync(context, next, clusterId).ConfigureAwait(false);
    }

    /// <summary>
    /// Core resilience logic, separated from YARP feature resolution for testability.
    /// </summary>
    internal async Task ExecuteWithResilienceAsync(HttpContext context, RequestDelegate next, string clusterId)
    {
        var policy = ResolvePolicy(clusterId);

        // Circuit breaker check — reject fast if circuit is open
        var isProbe = false;
        if (policy.CircuitBreakerEnabled)
        {
            var snapshot = await stateStore.GetSnapshotAsync(clusterId, context.RequestAborted).ConfigureAwait(false);

            if (snapshot.State == CircuitState.Open)
            {
                // Check if break duration elapsed → transition to half-open
                var now = TimeProvider.System.GetUtcNow();
                var transitioned = await stateStore.TryTransitionToHalfOpenAsync(clusterId, now, context.RequestAborted).ConfigureAwait(false);

                if (!transitioned)
                {
                    await RejectOpenCircuitAsync(context, clusterId, policy).ConfigureAwait(false);
                    return;
                }

                isProbe = true;
                logger.LogInformation("Circuit half-open for cluster {ClusterId} — allowing probe request", clusterId);
            }
            else if (snapshot.State == CircuitState.HalfOpen)
            {
                // Half-open admits exactly one request (the probe that won the transition above).
                // Letting everyone through would flood a backend that is still recovering.
                await RejectOpenCircuitAsync(context, clusterId, policy).ConfigureAwait(false);
                return;
            }
        }

        // Apply timeout if configured.
        // We intentionally overwrite context.RequestAborted with a linked token that has a
        // shorter deadline. The original token is preserved and restored in the finally block.
        // This is a documented pattern for injecting per-request timeouts into YARP pipelines.
        using var timeoutCts = policy.Timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted)
            : null;

        if (timeoutCts is not null && policy.Timeout.HasValue)
            timeoutCts.CancelAfter(policy.Timeout.Value);

        var originalToken = context.RequestAborted;
        if (timeoutCts is not null)
            context.RequestAborted = timeoutCts.Token;

        try
        {
            await next(context).ConfigureAwait(false);

            // Evaluate response — 5xx = failure for circuit breaker
            var statusCode = context.Response.StatusCode;
            var isFailure = statusCode >= policy.FailureStatusCodeMin && statusCode <= policy.FailureStatusCodeMax;

            if (policy.CircuitBreakerEnabled)
            {
                if (isFailure)
                {
                    await RecordFailureAsync(clusterId, policy, statusCode).ConfigureAwait(false);
                }
                else
                {
                    await stateStore.RecordSuccessAsync(clusterId).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !originalToken.IsCancellationRequested)
        {
            // Timeout — not client cancellation
            logger.LogWarning("Request to cluster {ClusterId} timed out after {Timeout}", clusterId, policy.Timeout);

            if (policy.CircuitBreakerEnabled)
                await RecordFailureAsync(clusterId, policy, 504).ConfigureAwait(false);

            // Guard against a proxied response that already began streaming before the timeout fired:
            // setting StatusCode after headers are sent throws InvalidOperationException (which would
            // reset the connection instead of producing a clean 504). Mirrors the backend-error branch.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                context.Response.ContentType = "application/json";
                var safeClusterId = JsonEncodedText.Encode(clusterId).ToString();
                await context.Response.WriteAsync(
                    $$$"""{"error":"gateway_timeout","cluster":"{{{safeClusterId}}}","timeout":"{{{policy.Timeout}}}"}""",
                    originalToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (originalToken.IsCancellationRequested)
        {
            // Client disconnected mid-request. Not a backend verdict — but an abandoned PROBE would
            // leave the circuit stuck HalfOpen (now rejecting everyone): re-arm it to Open so the
            // break-duration clock restarts and a new probe follows.
            if (isProbe && policy.CircuitBreakerEnabled)
                await stateStore.TransitionToAsync(clusterId, CircuitState.Open, policy.BreakDuration).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Backend connection failure
            logger.LogError(ex, "Proxy error for cluster {ClusterId}", clusterId);

            if (policy.CircuitBreakerEnabled)
                await RecordFailureAsync(clusterId, policy, 502).ConfigureAwait(false);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                context.Response.ContentType = "application/json";
                var safeClusterId = JsonEncodedText.Encode(clusterId).ToString();
                await context.Response.WriteAsync(
                    $$$"""{"error":"bad_gateway","cluster":"{{{safeClusterId}}}"}""",
                    originalToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Restore original token
            context.RequestAborted = originalToken;
        }
    }

    private async Task RejectOpenCircuitAsync(HttpContext context, string clusterId, ClusterResiliencePolicy policy)
    {
        logger.LogWarning("Circuit open for cluster {ClusterId} — rejecting request", clusterId);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = ((int)policy.BreakDuration.TotalSeconds).ToString();
        context.Response.ContentType = "application/json";
        // clusterId is gateway-internal (from YARP ClusterConfig), not user-supplied.
        // Sanitise it anyway to guard against misconfiguration with unusual chars.
        var safeClusterId = JsonEncodedText.Encode(clusterId).ToString();
        await context.Response.WriteAsync(
            $$$"""{"error":"circuit_open","cluster":"{{{safeClusterId}}}","retryAfter":{{{(int)policy.BreakDuration.TotalSeconds}}}}""",
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Extracts cluster ID from the YARP reverse proxy feature on the request.</summary>
    internal static string ResolveClusterId(HttpContext context)
    {
        var feature = context.Features.Get<Yarp.ReverseProxy.Model.IReverseProxyFeature>();
        return feature?.Cluster?.Config?.ClusterId ?? "unknown";
    }

    private ClusterResiliencePolicy ResolvePolicy(string clusterId)
    {
        return options.Clusters.TryGetValue(clusterId, out var clusterPolicy)
            ? clusterPolicy
            : options.Default;
    }

    private async Task RecordFailureAsync(string clusterId, ClusterResiliencePolicy policy, int statusCode)
    {
        await stateStore.RecordFailureAsync(clusterId).ConfigureAwait(false);

        var snapshot = await stateStore.GetSnapshotAsync(clusterId).ConfigureAwait(false);

        if (snapshot.FailureCount >= policy.FailureThreshold && snapshot.State == CircuitState.Closed)
        {
            await stateStore.TransitionToAsync(clusterId, CircuitState.Open, policy.BreakDuration).ConfigureAwait(false);
            logger.LogWarning(
                "Circuit opened for cluster {ClusterId} after {Failures} failures (last status: {StatusCode}). Break duration: {BreakDuration}",
                clusterId, snapshot.FailureCount, statusCode, policy.BreakDuration);
        }
    }
}
