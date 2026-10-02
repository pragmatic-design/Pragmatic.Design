using System.Text;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Routing;
using Pragmatic.Messaging.Sql;

namespace Pragmatic.Messaging.Tests.Sql;

/// <summary>
///     Shared SQL transport suite — the SAME behavior must hold on PostgreSQL and SQL Server
///     (concrete classes plug the provider). Runs the transport against the real database:
///     fan-out, point-to-point, backoff→dead-letter, delayed delivery with restart-safe cancel,
///     distributed request/reply.
/// </summary>
[Trait("Category", "Integration")]
public abstract class SqlTransportIntegrationSuite
{
    /// <summary>Null when Docker is unavailable (tests skip gracefully).</summary>
    protected abstract string? ConnectionString { get; }

    protected abstract void Configure(DbContextOptionsBuilder db);

    /// <param name="options">The options every context is created with.</param>
    /// <param name="hold">
    ///     When given, every asynchronous context creation waits for it: the transport's connect stops at its
    ///     first database call until the test lets it go.
    /// </param>
    private sealed class Factory(DbContextOptions<SqlTransportDbContext> options, Task? hold = null)
        : IDbContextFactory<SqlTransportDbContext>
    {
        public SqlTransportDbContext CreateDbContext() => new(options);

        public async Task<SqlTransportDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            if (hold is not null)
                await hold.WaitAsync(cancellationToken).ConfigureAwait(false);
            return CreateDbContext();
        }
    }

    private async Task<(SqlTransport Transport, SqlTransportStorage Storage, Factory ContextFactory, SqlTransportOptions Options)>
        ConnectAsync(Action<SqlTransportOptions>? tune = null)
    {
        var built = Build(tune);
        await built.Transport.ConnectAsync().ConfigureAwait(false);
        return built;
    }

    private (SqlTransport Transport, SqlTransportStorage Storage, Factory ContextFactory, SqlTransportOptions Options)
        Build(Action<SqlTransportOptions>? tune = null, Task? hold = null)
    {
        var options = new SqlTransportOptions
        {
            ConfigureDbContext = Configure,
            PollingInterval = TimeSpan.FromMilliseconds(200),
            MaxPollingInterval = TimeSpan.FromSeconds(1),
            LockDuration = TimeSpan.FromSeconds(30),
        };
        tune?.Invoke(options);

        var builder = new DbContextOptionsBuilder<SqlTransportDbContext>();
        Configure(builder);
        var factory = new Factory(builder.Options, hold);

        var storage = new SqlTransportStorage(factory, options, NullLogger<SqlTransportStorage>.Instance);
        var schema = new SqlTransportSchema(factory, NullLogger<SqlTransportSchema>.Instance);
        var transport = new SqlTransport(storage, schema, factory, options, NullLoggerFactory.Instance);
        return (transport, storage, factory, options);
    }

    /// <summary>
    ///     A publish that arrives while the connect is still preparing the schema waits for it,
    ///     instead of failing with "not connected".
    /// </summary>
    /// <remarks>
    ///     The connect is held at its first database call, so "while it connects" is a state the test
    ///     chooses and not a window it hopes to land in: left to run, the connect could finish before the
    ///     status was read, and on a fast enough machine it did.
    /// </remarks>
    [Fact]
    public async Task APublishIssuedWhileTheTransportConnects_WaitsForTheConnect()
    {
        if (ConnectionString is null)
            return;

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (transport, _, _, _) = Build(hold: hold.Task);
        try
        {
            var connecting = transport.ConnectAsync();
            transport.Status.Should().Be(TransportStatus.Connecting, "the connect is held before its first database call");

            var publishing = transport.PublishAsync("early"u8.ToArray(), $"t-{Guid.NewGuid():N}", MessageContext.New());
            publishing.IsCompleted.Should().BeFalse("a publish issued while the transport connects waits for the connect");

            hold.SetResult();
            await publishing.ConfigureAwait(true);
            await connecting.ConfigureAwait(true);
            transport.Status.Should().Be(TransportStatus.Connected);
        }
        finally
        {
            hold.TrySetResult();
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task PublishSubscribe_FanOut_DeliversToAllSubscriptions()
    {
        if (ConnectionString is null)
            return;

        var (transport, _, _, _) = await ConnectAsync();
        try
        {
            var topic = $"t-{Guid.NewGuid():N}";
            var receivedA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await transport.SubscribeAsync(topic, $"{topic}.a", (payload, _, _) =>
            {
                receivedA.TrySetResult(Encoding.UTF8.GetString(payload.Span));
                return Task.CompletedTask;
            });
            await transport.SubscribeAsync(topic, $"{topic}.b", (payload, _, _) =>
            {
                receivedB.TrySetResult(Encoding.UTF8.GetString(payload.Span));
                return Task.CompletedTask;
            });

            await transport.PublishAsync("fan-out"u8.ToArray(), topic, MessageContext.New());

            (await receivedA.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().Be("fan-out");
            (await receivedB.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().Be("fan-out");
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Send_PointToPoint_DeliversViaQueueConsumer()
    {
        if (ConnectionString is null)
            return;

        var (transport, _, _, _) = await ConnectAsync();
        try
        {
            var queue = $"q-{Guid.NewGuid():N}";
            var received = new TaskCompletionSource<(string Payload, MessageContext Context)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await ((IQueueConsumerTransport)transport).SubscribeQueueAsync(queue, (payload, ctx, _) =>
            {
                received.TrySetResult((Encoding.UTF8.GetString(payload.Span), ctx));
                return Task.CompletedTask;
            });

            var context = MessageContext.New(correlationId: "sql-p2p", tenantId: "acme");
            await transport.SendAsync("direct"u8.ToArray(), queue, context);

            var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            result.Payload.Should().Be("direct");
            result.Context.CorrelationId.Should().Be("sql-p2p");
            result.Context.TenantId.Should().Be("acme");
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task FailingHandler_BacksOff_ThenMovesToDeadLetterTable()
    {
        if (ConnectionString is null)
            return;

        // Tight knobs so the ladder completes fast: 2 deliveries then dead-letter.
        var (transport, _, contextFactory, _) = await ConnectAsync(o => o.MaxDeliveryCount = 2);
        try
        {
            var queue = $"q-{Guid.NewGuid():N}";
            var attempts = 0;

            await ((IQueueConsumerTransport)transport).SubscribeQueueAsync(queue, (_, _, _) =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("always fails");
            });

            await transport.SendAsync("poison"u8.ToArray(), queue, MessageContext.New());

            // First delivery is immediate; the second waits for ~2s backoff.
            var deadLettered = false;
            for (var i = 0; i < 100 && !deadLettered; i++)
            {
                await Task.Delay(200);
                var db = contextFactory.CreateDbContext();
                await using (db.ConfigureAwait(true))
                {
                    deadLettered = await db.DeadLetters.AnyAsync(d => d.QueueName == queue);
                }
            }

            deadLettered.Should().BeTrue("MaxDeliveryCount exceeded must MOVE the row to __TransportDeadLetters");
            attempts.Should().Be(2);

            var db2 = contextFactory.CreateDbContext();
            await using (db2.ConfigureAwait(true))
            {
                (await db2.Messages.CountAsync(m => m.QueueName == queue)).Should().Be(0, "the row moves, never copies");
            }
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scheduler_DelaysDelivery_AndCancelIsDurable()
    {
        if (ConnectionString is null)
            return;

        var (transport, storage, _, _) = await ConnectAsync();
        try
        {
            var topic = $"t-{Guid.NewGuid():N}";
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await transport.SubscribeAsync(topic, $"{topic}.s", (payload, _, _) =>
            {
                received.TrySetResult(Encoding.UTF8.GetString(payload.Span));
                return Task.CompletedTask;
            });

            var router = new FixedTopicRouter(topic);
            var scheduler = new SqlMessageScheduler(storage, router, new JsonMessageSerializer(),
                NullLogger<SqlMessageScheduler>.Instance);

            // Delayed delivery arrives after the delay…
            await scheduler.ScheduleAsync(new SqlPing("delayed"), TimeSpan.FromSeconds(1));
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().Contain("delayed");

            // …and cancellation is durable: a NEW scheduler instance (≈ process restart) can
            // cancel by token because the handle lives in the database, not in memory.
            var token = await scheduler.ScheduleAsync(new SqlPing("never"), TimeSpan.FromMinutes(10));
            var restartedScheduler = new SqlMessageScheduler(storage, router, new JsonMessageSerializer(),
                NullLogger<SqlMessageScheduler>.Instance);
            await restartedScheduler.CancelAsync(token);

            (await storage.CancelScheduledAsync(token)).Should().Be(0, "the restarted scheduler already deleted the rows");
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestReply_Distributed_WorksOverSqlQueues()
    {
        if (ConnectionString is null)
            return;

        var (transport, _, _, _) = await ConnectAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
            services.AddScoped<IRequestHandler<SqlQuoteRequest, SqlQuoteResponse>, SqlQuoteHandler>();
            await using var sp = services.BuildServiceProvider();

            var subscription = new Pragmatic.Messaging.RequestReply.RequestSubscription(
                typeof(SqlQuoteRequest),
                Pragmatic.Messaging.RequestReply.RequestReplyConventions.QueueFor(typeof(SqlQuoteRequest)),
                static async (provider, payload, context, ct) =>
                {
                    var serializer = provider.GetRequiredService<IMessageSerializer>();
                    var request = (SqlQuoteRequest)serializer.Deserialize(payload, typeof(SqlQuoteRequest))!;
                    var handler = provider.GetRequiredService<IRequestHandler<SqlQuoteRequest, SqlQuoteResponse>>();
                    var response = await handler.HandleAsync(request, context, ct).ConfigureAwait(false);
                    return serializer.Serialize(response, typeof(SqlQuoteResponse));
                });

            await Pragmatic.Messaging.RequestReply.RequestReplyBinder.BindAsync(
                transport, sp.GetRequiredService<IServiceScopeFactory>(), [subscription], NullLogger.Instance);

            var replyChannel = new Pragmatic.Messaging.RequestReply.TransportReplyChannel(
                transport, NullLogger<Pragmatic.Messaging.RequestReply.TransportReplyChannel>.Instance);

            try
            {
                // The responder provider (sp) HAS the handler — the requester uses a BARE
                // provider so the local fast path cannot win and the roundtrip goes over SQL.
                var response = await RequestViaBareProviderAsync(transport, replyChannel);
                response.Price.Should().Be(3.14m);
            }
            finally
            {
                await replyChannel.DisposeAsync();
            }
        }
        finally
        {
            await transport.DisposeAsync();
        }
    }

    private static async Task<SqlQuoteResponse> RequestViaBareProviderAsync(
        SqlTransport transport,
        Pragmatic.Messaging.RequestReply.TransportReplyChannel replyChannel)
    {
        var bareSp = new ServiceCollection()
            .AddSingleton<IMessageSerializer, JsonMessageSerializer>()
            .BuildServiceProvider();
        await using var _ = bareSp.ConfigureAwait(false);
        var bus = new TransportAwareMessageBus(
            transport,
            new DefaultMessageRouter(),
            bareSp.GetRequiredService<IMessageSerializer>(),
            new InMemoryMessageBus(bareSp, NullLogger<InMemoryMessageBus>.Instance, bareSp.GetServices<ITypedMessageDispatchTable>()),
            NullLogger<TransportAwareMessageBus>.Instance,
            replyChannel: replyChannel);

        return await bus.RequestAsync<SqlQuoteRequest, SqlQuoteResponse>(new SqlQuoteRequest("PI")).ConfigureAwait(false);
    }

    private sealed class FixedTopicRouter(string topic) : IMessageRouter
    {
        public string GetTopic<T>() where T : notnull => topic;
        public string GetTopic(Type messageType) => topic;
        public string GetSendQueue(Type messageType) => topic;
    }

    public sealed record SqlPing(string Text);

    public sealed record SqlQuoteRequest(string Symbol);

    public sealed record SqlQuoteResponse(string Symbol, decimal Price);

    private sealed class SqlQuoteHandler : IRequestHandler<SqlQuoteRequest, SqlQuoteResponse>
    {
        public Task<SqlQuoteResponse> HandleAsync(SqlQuoteRequest request, MessageContext context, CancellationToken ct)
            => Task.FromResult(new SqlQuoteResponse(request.Symbol, 3.14m));
    }
}
