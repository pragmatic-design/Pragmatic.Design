namespace Pragmatic.Gateway;

/// <summary>
///     A static route entry used as fallback when the Agent is unavailable.
/// </summary>
public sealed class RouteEntry
{
    /// <summary>Unique route identifier.</summary>
    public required string RouteId { get; set; }

    /// <summary>Request path pattern (e.g. <c>/api/{**catch-all}</c>).</summary>
    public required string Path { get; set; }

    /// <summary>
    ///     The addresses of the service's instances (e.g. <c>http://backend:5000</c>), one destination each.
    ///     At least one is required: a route with none fails the gateway's start, naming the route.
    /// </summary>
    public IList<string> Backends { get; set; } = [];

    /// <summary>
    ///     The prefix the gateway publishes the service under, removed before the request is forwarded
    ///     (YARP's <c>PathRemovePrefix</c>): <c>/orders/{**catch-all}</c> with <c>/orders</c> forwards
    ///     <c>/orders/health</c> as <c>/health</c>, so the service does not know where it is published.
    ///     <see cref="Path" /> has to start with it, or the gateway's start fails naming the route.
    /// </summary>
    public string? PathRemovePrefix { get; set; }

    /// <summary>Optional host constraint for the route match.</summary>
    public string? Host { get; set; }

    /// <summary>Whether the route requires authentication.</summary>
    public bool RequireAuth { get; set; }

    /// <summary>Per-route rate limit override (requests per minute). Null = global default.</summary>
    public int? RateLimit { get; set; }
}
