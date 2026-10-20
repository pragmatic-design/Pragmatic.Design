using Pragmatic.Messaging.Attributes;

namespace Pragmatic.Messaging.Fuzz;

/// <summary>Consumes <see cref="FuzzOrderPlaced" />, which is what puts it in the generated registry.</summary>
[MessageHandler]
public sealed partial class FuzzOrderHandler : IMessageHandler<FuzzOrderPlaced>
{
    public Task HandleAsync(FuzzOrderPlaced message, MessageContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}
