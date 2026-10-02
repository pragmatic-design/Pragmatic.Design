using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Multi-bus routing (PREVIEW). <see cref="NamedBusMessageBus" /> routes by
///     the <c>bus.name</c> header to a named <see cref="IMessageBus" />, falling
///     back to the default bus when the header is absent or unmatched. The
///     <c>[OnBus("name")]</c> attribute (Pragmatic.Messaging.Attributes) declares
///     a handler's target bus for the SG, and full DI wiring
///     (<c>MessagingBuilder.AddBus</c> materialising live named buses +
///     <c>IBusResolver</c>) is still a preview shape — so this sample composes
///     <see cref="NamedBusMessageBus" /> by hand with two real in-memory buses to
///     show the routing decision end-to-end.
/// </summary>
[Experimental("PRAGMSG_MULTIBUS")]
public static class MultiBusSample
{
    public sealed record AnalyticsEvent(string Name);

    public sealed class AnalyticsHandler : IMessageHandler<AnalyticsEvent>
    {
        public int Received { get; private set; }

        public Task HandleAsync(AnalyticsEvent message, MessageContext context, CancellationToken ct)
        {
            Received++;
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Multi-bus routing (preview) ---");

        var analyticsHandler = new AnalyticsHandler();

        // Build a dedicated provider for the "analytics" bus that knows the handler.
        var analyticsServices = new ServiceCollection();
        analyticsServices.AddLogging();
        analyticsServices.AddPragmaticMessaging();
        analyticsServices.AddSingleton<IMessageHandler<AnalyticsEvent>>(analyticsHandler);
        await using var analyticsSp = analyticsServices.BuildServiceProvider();
        var analyticsBus = analyticsSp.GetRequiredService<IMessageBus>();

        // Default bus has no analytics handler registered.
        var defaultServices = new ServiceCollection();
        defaultServices.AddLogging();
        defaultServices.AddPragmaticMessaging();
        await using var defaultSp = defaultServices.BuildServiceProvider();
        var defaultBus = defaultSp.GetRequiredService<IMessageBus>();

        // Compose the named-bus router: bus.name == "analytics" -> analyticsBus.
        var named = new NamedBusMessageBus(
            defaultBus,
            new Dictionary<string, IMessageBus> { ["analytics"] = analyticsBus });

        // Header-routed: goes to the analytics bus (and its handler).
        var routed = MessageContext.New() with
        {
            Headers = new Dictionary<string, string> { ["bus.name"] = "analytics" }
        };
        await named.PublishAsync(new AnalyticsEvent("page_view"), routed);

        // No header -> routed to the default bus (no analytics handler -> not received).
        await named.PublishAsync(new AnalyticsEvent("ignored"), MessageContext.New());

        Console.WriteLine($"  analytics handler received: {analyticsHandler.Received} (expected 1 — only the bus.name=analytics message)");
        Console.WriteLine("  attribute note           : annotate a handler with [OnBus(\"analytics\")] so the SG/IBusResolver");
        Console.WriteLine("                             will route to the named bus automatically once AddBus wiring is final.");
        Console.WriteLine();
    }
}
