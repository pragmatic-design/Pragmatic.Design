using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.ClaimCheck;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Claim check: payloads above <c>Threshold</c> are stored on the app's
///     <see cref="IFileStorage" /> (here: local disk in a temp folder) and the message
///     travels as an empty stub with the <c>x-claim-check</c> reference header; the consumer
///     retrieves transparently before deserialization and deletes the blob after a
///     successful dispatch. The handler never knows the payload took a detour.
/// </summary>
public static class ClaimCheckSample
{
    public sealed record ReportGenerated(string Body);

    public sealed class ReportHandler : IMessageHandler<ReportGenerated>
    {
        public static readonly TaskCompletionSource<int> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(ReportGenerated message, MessageContext context, CancellationToken ct)
        {
            Received.TrySetResult(message.Body.Length);
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Claim check (large payloads on IFileStorage) ---");

        var basePath = Path.Combine(Path.GetTempPath(), $"claim-check-sample-{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFileStorage>(new LocalDiskFileStorage(basePath, NullLogger<LocalDiskFileStorage>.Instance));
            services.AddScoped<IMessageHandler<ReportGenerated>, ReportHandler>();
            services.AddPragmaticMessaging(msg => msg
                .UseChannels()
                .EnableClaimCheck(o => o.Threshold = 1024));   // 1 KB for the demo (default 256 KB)
            services.AddSingleton(new MessageSubscription(typeof(ReportGenerated), subscriber: "reports"));

            var provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                var consumer = provider.GetServices<IHostedService>().OfType<ChannelConsumerService>().Single();
                await consumer.StartAsync(CancellationToken.None);

                using var scope = provider.CreateScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new ReportGenerated(new string('x', 50_000)));

                var receivedLength = await ReportHandler.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Console.WriteLine($"  handler received the FULL {receivedLength:N0}-char payload");
                Console.WriteLine("  on the wire: empty stub + x-claim-check header; blob deleted after consume");

                await consumer.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, recursive: true);
        }

        Console.WriteLine();
    }
}
