using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Routing;
using Pragmatic.Messaging.Sql;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     SQL transport: PostgreSQL/SQL Server tables as the broker — durable subscriptions,
///     lease-based competing consumers, backoff + dead-letter table, native restart-safe
///     scheduler. The demo runs the SAME engine on in-memory SQLite (the schema and the
///     CAS claim are provider-portable); production points <c>ConfigureDbContext</c> at
///     <c>UseNpgsql</c> (with pg_notify wakeups) or <c>UseSqlServer</c>.
/// </summary>
public static class SqlTransportSample
{
    public sealed record StockUpdated(string Sku, int Quantity);

    public sealed class StockProjectionHandler : IMessageHandler<StockUpdated>
    {
        public static readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(StockUpdated message, MessageContext context, CancellationToken ct)
        {
            Console.WriteLine($"  [consumer] {message.Sku} → {message.Quantity} (row claimed via lease, then DELETEd on ack)");
            Done.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- SQL transport (tables as broker; demo on SQLite, prod on Postgres/SqlServer) ---");

        // Shared in-memory SQLite: the connection must outlive every DbContext the factory creates.
        var connection = new SqliteConnection("DataSource=sql-transport-sample;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<IMessageHandler<StockUpdated>, StockProjectionHandler>();
            services.AddPragmaticMessaging(msg => msg.UseSqlTransport(o =>
            {
                o.ConfigureDbContext = db => db.UseSqlite(connection);
                o.PollingInterval = TimeSpan.FromMilliseconds(100);   // demo latency; prod default 1s + pg_notify
            }));
            services.AddSingleton(new MessageSubscription(typeof(StockUpdated), subscriber: "warehouse"));

            var provider = services.BuildServiceProvider();
            await using (provider.ConfigureAwait(false))
            {
                // What SqlConsumerService does at host startup, made explicit for the demo:
                // connect (creates the __Transport* tables idempotently), then bind the
                // subscription (durable row in __TransportSubscriptions + claim loop).
                var transport = provider.GetRequiredService<SqlTransport>();
                await transport.ConnectAsync();
                var handles = await TransportSubscriptionBinder.BindAsync(
                    transport,
                    provider.GetRequiredService<IMessageRouter>(),
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetServices<MessageSubscription>());

                using var scope = provider.CreateScope();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

                // Publish = one row per durable subscription, in the same transaction.
                await bus.PublishAsync(new StockUpdated("SKU-42", 17));
                await StockProjectionHandler.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));

                // Native scheduler: VisibleAt in the future + SchedulingTokenId; cancel is a
                // DELETE by token — durable and restart-safe (no broker handle to lose).
                var scheduler = provider.GetRequiredService<IMessageScheduler>();
                var scheduleId = await scheduler.ScheduleAsync(new StockUpdated("SKU-42", 0), TimeSpan.FromHours(1));
                await scheduler.CancelAsync(scheduleId);
                Console.WriteLine($"  [scheduler] scheduled {scheduleId:N} for +1h, then cancelled durably (row deleted)");

                foreach (var handle in handles)
                    await handle.DisposeAsync();
                await transport.DisconnectAsync();
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }

        Console.WriteLine();
    }
}
