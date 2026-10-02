using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.ClaimCheck;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     Full claim check flow over the real Channel transport with the real
///     <see cref="LocalDiskFileStorage"/>: publish above threshold → payload offloaded to disk,
///     stub on the wire → consumer retrieves, dispatches, and deletes the blob.
/// </summary>
#pragma warning disable CA2007 // xUnit manages SynchronizationContext
public sealed class ClaimCheckEndToEndTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"claimcheck-e2e-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }

    [Fact]
    public async Task Publish_LargePayloadOverChannels_ConsumerReceivesFullMessageAndBlobIsDeleted()
    {
        var received = new TaskCompletionSource<LargeDocument>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileStorage>(new LocalDiskFileStorage(_basePath, NullLogger<LocalDiskFileStorage>.Instance));
        services.AddScoped<IMessageHandler<LargeDocument>>(_ => new LargeDocumentHandler(received));
        services.AddPragmaticMessaging(msg => msg
            .UseChannels()
            .EnableClaimCheck(o => { o.Threshold = 1024; o.DeleteAfterConsume = true; }));
        services.AddSingleton(new MessageSubscription(typeof(LargeDocument), subscriber: "claim-check"));

        var sp = services.BuildServiceProvider();
        await using (sp.ConfigureAwait(false))
        {
            var consumer = sp.GetServices<IHostedService>().OfType<ChannelConsumerService>().Single();
            await consumer.StartAsync(CancellationToken.None);
            try
            {
                var bus = sp.CreateScope().ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new LargeDocument(new string('d', 100_000)));

                var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
                message.Body.Should().HaveLength(100_000);

                // DeleteAfterConsume (opted in above): the blob must be gone after a successful dispatch.
                var container = Path.Combine(_basePath, "files", FileStorageClaimCheckStore.Container);
                await WaitForEmptyAsync(container);
            }
            finally
            {
                await consumer.StopAsync(CancellationToken.None);
            }
        }
    }

    // Deletion happens after the handler completes — poll briefly instead of a blind delay.
    private static async Task WaitForEmptyAsync(string directory)
    {
        for (var i = 0; i < 50; i++)
        {
            if (!Directory.Exists(directory) || Directory.GetFiles(directory).Length == 0)
                return;
            await Task.Delay(100);
        }

        Directory.GetFiles(directory).Should().BeEmpty("the claim check blob must be deleted after consume");
    }

    private sealed class LargeDocumentHandler(TaskCompletionSource<LargeDocument> received) : IMessageHandler<LargeDocument>
    {
        public Task HandleAsync(LargeDocument message, MessageContext context, CancellationToken ct)
        {
            received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }
}

public sealed record LargeDocument(string Body);
