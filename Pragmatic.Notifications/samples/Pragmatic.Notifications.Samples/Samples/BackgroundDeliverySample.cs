using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Extensions;

namespace Pragmatic.Notifications.Samples.Samples;

/// <summary>
///     EnqueueAsync writes the request to a bounded channel and returns immediately with a tracking
///     id; the registered NotificationDeliveryWorker (an IHostedService) consumes the channel in the
///     background and runs each request through the full pipeline. This sample builds a real host,
///     starts the hosted services, enqueues several notifications, and waits for the worker to deliver
///     them — demonstrating the fire-and-forget path that AddPragmaticNotifications wires up by default.
/// </summary>
public static class BackgroundDeliverySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Background delivery worker (EnqueueAsync) ---");

        var channel = new CountingChannel();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders(); // keep sample output clean
        builder.Services.AddPragmaticNotifications(n => n
            .AddChannel(channel)
            .UseInMemoryStore());

        using var host = builder.Build();
        await host.StartAsync(); // starts NotificationDeliveryWorker

        // INotificationService is scoped — its resolver and preferences read the database — so outside a
        // request it is taken from a scope, as any background caller would. Resolved from the root it
        // threw in Development, where the host validates scopes.
        using var scope = host.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        const int total = 5;
        for (var i = 1; i <= total; i++)
        {
            var ack = await notifications.EnqueueAsync(new NotificationRequest
            {
                Audience = NotificationAudience.EndUser,
                Recipient = new NotificationRecipient { EmailAddress = $"user{i}@example.com" },
                Content = new NotificationContent { Subject = $"Queued #{i}", Body = "delivered in background" },
            });
            Console.WriteLine($"  enqueued #{i}  ack={(ack.Success ? "OK" : "FAIL")}  id={ack.NotificationId}");
        }

        // Wait for the background worker to drain the queue.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (channel.Count < total && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        Console.WriteLine($"  delivered by worker : {channel.Count}/{total}");

        await host.StopAsync();
        Console.WriteLine();
    }

    private sealed class CountingChannel : INotificationChannel
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public NotificationChannel Channel => NotificationChannel.Email;

        public Task<DeliveryResult> DeliverAsync(
            ResolvedRecipient recipient,
            NotificationContent content,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(DeliveryResult.Succeeded());
        }
    }
}
