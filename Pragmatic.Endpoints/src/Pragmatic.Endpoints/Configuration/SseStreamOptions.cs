namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Per-endpoint SSE stream behavior (from [Sse]).
/// </summary>
public sealed record SseStreamOptions
{
    /// <summary>
    ///     Seconds of idle time before a keep-alive comment (<c>: hb</c>) is written.
    ///     Null or 0 disables the heartbeat (default).
    /// </summary>
    public int? HeartbeatSeconds { get; init; }
}
