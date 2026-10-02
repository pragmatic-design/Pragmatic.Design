namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for domain event dispatch and handling.
/// </summary>
public static class EventTags
{
    /// <summary>The event type name (e.g., "OrderCreatedEvent").</summary>
    public const string Name = "pragmatic.event.name";

    /// <summary>Number of handlers invoked for the event.</summary>
    public const string HandlerCount = "pragmatic.event.handler_count";

    /// <summary>Number of handlers that threw while handling the event.</summary>
    public const string HandlerFailures = "pragmatic.event.handler_failures";
}
