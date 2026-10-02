using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;

namespace Pragmatic.Messaging.Tests.Unit;

public class KillSwitchTests
{
    [Fact]
    public void EnableKillSwitch_RegistersOptions()
    {
        var services = new ServiceCollection();
        new MessagingBuilder(services).EnableKillSwitch(o => o.ActivationThreshold = 3);

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<KillSwitchOptions>().ActivationThreshold.Should().Be(3);
    }

    [Fact]
    public async Task Binder_ConsecutiveFailuresBeyondThreshold_PausesConsumption()
    {
        var services = new ServiceCollection();
        services.AddPragmaticMessaging();
        // Every dispatch fails: the handler throws through the bus.
        services.AddScoped<IMessageHandler<KillSwitchProbe>, ThrowingProbeHandler>();
        await using var sp = services.BuildServiceProvider();

        var transport = new CapturingTransport();
        var killSwitch = new KillSwitchOptions
        {
            ActivationThreshold = 2,
            TripDuration = TimeSpan.FromMilliseconds(300),
        };

        await TransportSubscriptionBinder.BindAsync(
            transport,
            new DefaultMessageRouter(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            [new MessageSubscription(typeof(KillSwitchProbe), subscriber: "kill-switch")],
            killSwitch,
            NullLogger.Instance);

        var payload = new JsonMessageSerializer().Serialize(new KillSwitchProbe(1));

        // Two consecutive failures reach the threshold (each dispatch throws).
        for (var i = 0; i < 2; i++)
        {
            var act = () => transport.Handler!(payload, MessageContext.New(), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // Third delivery: the switch is tripped — it must pause ~TripDuration before attempting.
        var stopwatch = Stopwatch.StartNew();
        var tripped = () => transport.Handler!(payload, MessageContext.New(), CancellationToken.None);
        await tripped.Should().ThrowAsync<InvalidOperationException>();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    private sealed class CapturingTransport : IMessageTransport
    {
        public Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task>? Handler { get; private set; }

        public string Name => "Capturing";
        public TransportStatus Status => TransportStatus.Connected;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IAsyncDisposable> SubscribeAsync(
            string topic,
            string subscriptionName,
            Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
            CancellationToken ct = default)
        {
            Handler = handler;
            return Task.FromResult<IAsyncDisposable>(new Noop());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingProbeHandler : IMessageHandler<KillSwitchProbe>
    {
        public Task HandleAsync(KillSwitchProbe message, MessageContext context, CancellationToken ct)
            => throw new InvalidOperationException("downstream broken");
    }
}

public sealed record KillSwitchProbe(int Id);
