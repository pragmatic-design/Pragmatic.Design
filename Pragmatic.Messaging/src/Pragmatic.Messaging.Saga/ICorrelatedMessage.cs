namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Marker interface for events/messages that carry a correlation identifier used to
///     route subsequent events to the same saga instance.
/// </summary>
/// <remarks>
///     <para>
///         The SG-generated <c>SagaEventHandler&lt;TEvent&gt;</c> reads <see cref="CorrelationId"/>
///         off the incoming message and calls <c>{Saga}.Orchestrator.HandleEventAsync(event, type, correlationId, ct)</c>.
///         Every event consumed by a <c>[Saga&lt;TState&gt;]</c> must implement this interface
///         OR mark a property with <c>[CorrelationKey]</c> (compile error PRAG0820 otherwise).
///     </para>
///     <para>
///         For records with positional parameters the implementation is typically a property
///         projection: <c>public string CorrelationId =&gt; OrderId.ToString();</c>.
///     </para>
/// </remarks>
public interface ICorrelatedMessage
{
    /// <summary>
    ///     Stable identifier shared across all events that belong to the same saga instance.
    ///     Must be the same value for every event of a given conversation.
    /// </summary>
    string CorrelationId { get; }
}
