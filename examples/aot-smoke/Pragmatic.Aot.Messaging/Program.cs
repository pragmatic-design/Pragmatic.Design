// Native AOT smoke for the in-memory message bus: a message dispatched untyped reaches its generated [MessageHandler] through the
// generated dispatch table. Prints AOT-MESSAGING-OK and exits 0 when the handler received it; exits 1 otherwise.

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Aot.Messaging;
using Pragmatic.Aot.Messaging.Generated;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddPragmaticMessaging();
services.AddPragmaticMessageHandlers();
services.AddSingleton<Received>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
// Untyped: the path that probes the generated dispatch tables, and that fell back to the dynamic binder when none knew the type.
await bus.DispatchAsync(new OrderPlaced("o-1"), MessageContext.New());

var received = provider.GetRequiredService<Received>();
if (received.Orders.SequenceEqual(["o-1"]))
{
    Console.WriteLine("ok   the message reached its handler through the generated table");
    Console.WriteLine("AOT-MESSAGING-OK");
    return 0;
}

Console.WriteLine($"FAIL the handler received [{string.Join(", ", received.Orders)}], expected [o-1]");
return 1;
