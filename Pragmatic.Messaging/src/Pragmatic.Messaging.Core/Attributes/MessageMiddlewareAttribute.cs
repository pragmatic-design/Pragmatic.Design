namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Registers this class as a message middleware. It must implement <see cref="IMessageMiddleware"/>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The registration is the attribute's whole effect, and its absence has no failure to
///         observe: the messages are still handled, just not wrapped. A shape diagnostic that checks
///         the class implements the interface is not a reader of the declaration.
///     </para>
///     <para>
///         Ordering is <see cref="IMessageMiddleware.Order"/> and is not repeated here: the pipeline
///         reads the interface's, and a second place to say it would be one that does nothing.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageMiddlewareAttribute : Attribute
{
    /// <summary>
    ///     When set, the middleware runs only for this message type; when null, for every message.
    /// </summary>
    /// <remarks>
    ///     The generator wraps the registration in <see cref="MessageTypeScopedMiddleware"/>, so the
    ///     comparison happens on a type it already knows rather than by reading this attribute at run
    ///     time. ⚠️ The match is on the exact type: a middleware declared for a base type would
    ///     otherwise run for every derived message with no way to scope it back down.
    /// </remarks>
    public Type? ForMessageType { get; set; }
}
