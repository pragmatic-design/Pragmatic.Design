namespace Pragmatic.Gateway;

/// <summary>
///     Well-known keys stored in a YARP route's <c>Metadata</c> by the Gateway, read back in the
///     reverse-proxy pipeline.
/// </summary>
internal static class GatewayRouteMetadata
{
    /// <summary>The roster app id a route targets — drives per-app maintenance (GW-M3).</summary>
    public const string AppId = "appId";

    /// <summary>Per-route rate limit override (requests/minute) — read by the global rate limiter.</summary>
    public const string RateLimit = "rateLimit";
}
