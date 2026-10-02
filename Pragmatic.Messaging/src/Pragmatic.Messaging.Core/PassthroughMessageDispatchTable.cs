namespace Pragmatic.Messaging;

/// <summary>
///     Default passthrough implementation of <see cref="ITypedMessageDispatchTable" />.
///     Returns null for all message types, indicating no compile-time dispatch is available.
///     The SG-generated implementation replaces this when available.
/// </summary>
internal sealed class PassthroughMessageDispatchTable : ITypedMessageDispatchTable
{
    public Task? TryDispatch(IMessageBus bus, object message, MessageContext context, CancellationToken ct)
        => null;
}
