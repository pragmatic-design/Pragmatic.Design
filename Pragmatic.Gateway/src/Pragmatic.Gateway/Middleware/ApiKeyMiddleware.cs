namespace Pragmatic.Gateway.Middleware;

/// <summary>
///     Rejects requests that do not carry a valid API key in the configured header
///     (<see cref="ApiKeyOptions.HeaderName"/>, default <c>X-Api-Key</c>). Only wired when
///     <c>Gateway:ApiKey</c> is configured with at least one valid key; otherwise it is a no-op.
///     Keys are compared in constant time to avoid leaking them via response timing.
/// </summary>
internal sealed class ApiKeyMiddleware(RequestDelegate next, ApiKeyOptions options, ILogger<ApiKeyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Not configured (no valid keys) → don't gate anything.
        if (options.ValidKeys.Count == 0)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Never gate the health endpoint — a liveness probe must reach it without an API key,
        // otherwise the orchestrator marks the gateway unhealthy and restarts it.
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(options.HeaderName, out var provided)
            || provided.Count == 0
            || !IsValidKey(provided.ToString()))
        {
            logger.LogWarning("Gateway request rejected: missing or invalid API key header '{Header}'.", options.HeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or missing API key.").ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private bool IsValidKey(string provided)
    {
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        foreach (var key in options.ValidKeys)
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, keyBytes))
                return true;
        }

        return false;
    }
}
