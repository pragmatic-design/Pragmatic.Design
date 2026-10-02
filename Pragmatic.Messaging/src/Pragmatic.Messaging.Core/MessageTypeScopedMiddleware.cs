namespace Pragmatic.Messaging;

/// <summary>
///     Runs the middleware it wraps only for one message type, and steps aside for every other.
/// </summary>
/// <remarks>
///     <para>
///         What <c>[MessageMiddleware(ForMessageType = typeof(T))]</c> means, as an object. The generator
///         emits the wrapper with the declared type because it knows it at compile time; the pipeline
///         then holds an ordinary <see cref="IMessageMiddleware" /> and asks nothing about it.
///     </para>
///     <para>
///         ⚠️ The alternative was for the pipeline to read the attribute at run time, which is a
///         <c>GetCustomAttribute</c> on every message — reflective, and a decision taken where it is
///         already known. Deciding it at registration is the same rule as everywhere else: what the
///         generator knows, the generated code says ("decide at compile time").
///     </para>
///     <para>
///         ⚠️ Exact type, not assignability. A middleware declared for a base type would otherwise run
///         for every derived message and be impossible to scope back down, and message types in this
///         framework are the contract rather than a hierarchy. <see cref="Order" /> is the inner
///         middleware's, so scoping one does not move it in the chain.
///     </para>
/// </remarks>
/// <param name="inner">The middleware to run when the message matches.</param>
/// <param name="messageType">The one message type it applies to.</param>
public sealed class MessageTypeScopedMiddleware(IMessageMiddleware inner, Type messageType) : IMessageMiddleware
{
    private readonly IMessageMiddleware _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly Type _messageType = messageType ?? throw new ArgumentNullException(nameof(messageType));

    /// <summary>The wrapped middleware's own order: scoping it does not move it.</summary>
    public int Order => _inner.Order;

    /// <summary>The middleware it wraps, so a test can say which one this is.</summary>
    public IMessageMiddleware Inner => _inner;

    /// <summary>The message type it applies to.</summary>
    public Type MessageType => _messageType;

    /// <inheritdoc />
    public Task InvokeAsync<T>(
        T message, MessageContext context, MessageHandlerDelegate next, CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(next);

        return typeof(T) == _messageType
            ? _inner.InvokeAsync(message, context, next, ct)
            : next();
    }
}
