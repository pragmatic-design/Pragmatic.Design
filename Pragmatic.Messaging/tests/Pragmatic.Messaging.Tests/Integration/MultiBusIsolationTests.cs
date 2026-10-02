using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Extensions;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Multi-bus with ISOLATED transports (W5, out of experimental): a named bus gets its own
///     keyed transport + consumer service; the composite routes `bus.name`-addressed publishes
///     to it, and only that bus's handlers consume ([OnBus] resolver filtering).
/// </summary>
public class MultiBusIsolationTests
{
    public sealed record AnalyticsEvent(string Name);

    private sealed class AnalyticsHandler : IMessageHandler<AnalyticsEvent>
    {
        public static readonly List<AnalyticsEvent> Received = [];
        public static readonly TaskCompletionSource FirstReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(AnalyticsEvent message, MessageContext context, CancellationToken ct)
        {
            lock (Received)
            {
                Received.Add(message);
            }

            FirstReceived.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Stands in for the SG-generated [OnBus] resolver.</summary>
    private sealed class AnalyticsBusResolver : IBusResolver
    {
        public string? GetBusName(string handlerTypeName)
            => handlerTypeName == typeof(AnalyticsHandler).FullName ? "analytics" : null;
    }

    [Fact]
    public async Task NamedBus_WithIsolatedTransport_ConsumesOnItsOwnTransport()
    {
        var defaultTransport = new ChannelTransport(new ChannelOptions(), NullLogger<ChannelTransport>.Instance);
        var analyticsTransport = new ChannelTransport(new ChannelOptions(), NullLogger<ChannelTransport>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging(m =>
        {
            m.AddBus("analytics", b => b.UseTransport(_ => analyticsTransport, "channels"));
        });
        // Default transport for the default bus (registered directly for test control).
        services.AddSingleton<IMessageTransport>(defaultTransport);
        services.AddSingleton<IBusResolver>(new AnalyticsBusResolver());
        services.AddSingleton<IMessageHandler<AnalyticsEvent>, AnalyticsHandler>();
        // ⚠️ AddMessageSubscriptionOnBus, not AddMessageSubscription: the plain one now takes the
        // subscriber as its second argument, so `AddMessageSubscription<T>("analytics")` compiles and
        // registers a DEFAULT-bus subscription named "analytics" — the reason the bus overload was
        // given a name of its own rather than a second string parameter.
        services.AddMessageSubscriptionOnBus<AnalyticsEvent>("analytics", subscriber: "messaging-tests");

        await using var sp = services.BuildServiceProvider();

        // The keyed transport is the ISOLATED instance, not the default one.
        sp.GetRequiredKeyedService<IMessageTransport>("analytics").Should().BeSameAs(analyticsTransport);

        // Composite bus in place of the default registration.
        var bus = sp.GetRequiredService<IMessageBus>();
        bus.Should().BeOfType<NamedBusMessageBus>();

        // Start the named-bus consumer (what the host does).
        var consumers = sp.GetServices<IHostedService>().OfType<NamedBusConsumerService>().ToList();
        consumers.Should().ContainSingle();
        await consumers[0].StartAsync(CancellationToken.None);

        try
        {
            // ⚠️ Wait for the bind before publishing. StartAsync on a BackgroundService returns as soon
            // as ExecuteAsync yields, and the subscription is bound after that — so a publish issued
            // straight away can beat it. A topic fans out to one channel per
            // subscription, so a message published before anyone is subscribed has nowhere to go and is
            // discarded, exactly as a topic exchange with no bound queue discards it. Before that it sat
            // in the topic's shared channel and the race was invisible; the race was always there.
            for (var attempt = 0; attempt < 100 && analyticsTransport.GetChannelNames().Count == 0; attempt++)
                await Task.Delay(20);

            analyticsTransport.GetChannelNames().Should().NotBeEmpty(
                "the named bus's consumer bound its subscription — without it the publish below is lost");

            // Publish addressed to the analytics bus → isolated transport → its consumer → handler.
            var context = MessageContext.New() with
            {
                Headers = new Dictionary<string, string> { ["bus.name"] = "analytics" },
            };
            await bus.PublishAsync(new AnalyticsEvent("page-view"), context);

            await AnalyticsHandler.FirstReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AnalyticsHandler.Received.Should().ContainSingle().Which.Name.Should().Be("page-view");

            // Isolation: nothing traveled on the default transport's channels.
            defaultTransport.GetChannelNames().Should().BeEmpty();
        }
        finally
        {
            await consumers[0].StopAsync(CancellationToken.None);
            await defaultTransport.DisposeAsync();
        }
    }
}
