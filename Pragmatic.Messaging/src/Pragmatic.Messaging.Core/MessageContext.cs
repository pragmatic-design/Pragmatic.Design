namespace Pragmatic.Messaging;

/// <summary>
///     Metadata associated with a message during dispatch.
/// </summary>
/// <param name="MessageId">Unique identifier for this message instance.</param>
/// <param name="CorrelationId">Correlation identifier for tracing across boundaries.</param>
/// <param name="TenantId">Tenant identifier for multi-tenant scenarios.</param>
/// <param name="UserId">Identifier of the user that originated the message.</param>
/// <param name="Headers">Extensible key-value headers.</param>
/// <param name="RetryCount">Number of retry attempts so far (0 = first attempt).</param>
/// <param name="BusName">Target bus name for multi-bus scenarios (null = default bus).</param>
/// <param name="SourceBoundary">Boundary that produced this message.</param>
/// <param name="EnqueuedAt">When the message entered the transport.</param>
/// <param name="MessageType">
///     The fully qualified name of the type that was published.
///     <para>
///         ⚠️ <b>A topic is a boundary, not a type</b> (<c>DefaultMessageRouter.GetTopic</c> answers
///         <c>{boundary}.events</c>), so every subscription of that boundary is delivered every message
///         of it. Without this, a consumer deserializes whatever arrives into the type its subscription
///         was created for — which produces an object of the right shape and the wrong content, not an
///         error. <c>TransportSubscriptionBinder</c> compares this against its own type and skips what
///         is not its own.
///     </para>
///     <para>
///         Null when the publisher did not say — a message from an older publisher, or a transport
///         that does not carry it. A consumer that cannot tell must not refuse, so an absent value
///         means "deliver".
///     </para>
/// </param>
public sealed record MessageContext(
    string MessageId,
    string? CorrelationId = null,
    string? TenantId = null,
    string? UserId = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    int RetryCount = 0,
    string? BusName = null,
    string? SourceBoundary = null,
    DateTimeOffset? EnqueuedAt = null,
    string? MessageType = null)
{
    /// <summary>
    ///     Creates a new context with a generated MessageId.
    /// </summary>
    public static MessageContext New(
        string? correlationId = null,
        string? tenantId = null,
        string? userId = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(
            MessageId: Guid.NewGuid().ToString("N"),
            CorrelationId: correlationId,
            TenantId: tenantId,
            UserId: userId,
            Headers: headers);

    /// <summary>
    ///     Creates a context for retry with incremented count.
    /// </summary>
    public MessageContext ForRetry() => this with { RetryCount = RetryCount + 1 };
}
