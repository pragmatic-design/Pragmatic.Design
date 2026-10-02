using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using ChannelOptions = Pragmatic.Messaging.Channels.ChannelOptions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Real Channel transport integration tests — verifies actual async
///     producer-consumer flow via System.Threading.Channels.
/// </summary>
#pragma warning disable CA2007 // xUnit manages SynchronizationContext
public class ChannelsIntegrationTests
{
    [Fact]
    public async Task FullFlow_PublishSubscribeMultipleConsumers()
    {
        var options = new ChannelOptions { Capacity = 100, ConsumerCount = 3 };
        await using var transport = new ChannelTransport(options, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();

        var received = new System.Collections.Concurrent.ConcurrentBag<string>();
        var allDone = new TaskCompletionSource();

        await transport.SubscribeAsync("load-test", "multi-consumer", (payload, _, _) =>
        {
            received.Add(Encoding.UTF8.GetString(payload.Span));
            if (received.Count >= 100)
                allDone.TrySetResult();
            return Task.CompletedTask;
        });

        // Publish 100 messages
        for (var i = 0; i < 100; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"msg-{i}");
            await transport.PublishAsync(bytes, "load-test", MessageContext.New());
        }

        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().HaveCount(100);
    }

    [Fact]
    public async Task Backpressure_WhenFull_ShouldWait()
    {
        // Tiny capacity to test backpressure
        var options = new ChannelOptions
        {
            Capacity = 5,
            ConsumerCount = 0, // No consumer — messages accumulate
            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropNewest
        };
        await using var transport = new ChannelTransport(options, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();

        // Fill the channel beyond capacity — DropNewest means no blocking
        for (var i = 0; i < 20; i++)
        {
            await transport.PublishAsync(new byte[] { (byte)i }, "full-topic", MessageContext.New());
        }

        // Should not throw — DropNewest just discards
        transport.GetPendingCount("full-topic").Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task SubscriptionDispose_ShouldStopConsuming()
    {
        var options = new ChannelOptions { Capacity = 100, ConsumerCount = 1 };
        await using var transport = new ChannelTransport(options, NullLogger<ChannelTransport>.Instance);
        await transport.ConnectAsync();

        var count = 0;
        var sub = await transport.SubscribeAsync("dispose-topic", "dispose-sub", (_, _, _) =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        // Publish some
        for (var i = 0; i < 5; i++)
            await transport.PublishAsync(new byte[] { 1 }, "dispose-topic", MessageContext.New());

        await Task.Delay(200); // Let consumer process

        // Dispose subscription
        await sub.DisposeAsync();

        var countAfterDispose = count;
        await Task.Delay(100);

        // Publish more — should not be processed
        for (var i = 0; i < 5; i++)
            await transport.PublishAsync(new byte[] { 1 }, "dispose-topic", MessageContext.New());

        await Task.Delay(200);

        // Count should not have increased significantly after dispose
        count.Should().BeLessOrEqualTo(countAfterDispose + 1); // ±1 for race
    }
}
