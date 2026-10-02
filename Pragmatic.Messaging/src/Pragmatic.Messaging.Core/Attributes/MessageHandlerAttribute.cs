namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a class as a message handler for source generator discovery.
///     The class must implement <see cref="IMessageHandler{T}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageHandlerAttribute : Attribute
{
    /// <summary>
    ///     Execution order among handlers for the same message type.
    ///     Lower values execute first. Default is 0.
    /// </summary>
    public int Order { get; set; }
}
