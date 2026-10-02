namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Tunes Server-Sent Events behavior for a streaming endpoint.
/// </summary>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/feed")]
/// [Sse(HeartbeatSeconds = 15)]
/// public partial class FeedEndpoint : StreamingEndpoint&lt;FeedItemDto&gt; { ... }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SseAttribute : Attribute
{
    /// <summary>Seconds of idle time before a keep-alive comment is written; 0 disables it.</summary>
    public int HeartbeatSeconds { get; set; }
}
