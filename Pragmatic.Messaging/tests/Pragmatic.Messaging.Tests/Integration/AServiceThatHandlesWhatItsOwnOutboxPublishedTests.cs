using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Events;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.EFCore.Outbox;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Messaging.Routing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     A service that publishes a message through its own outbox and handles it itself
///     receives it, exactly once.
/// </summary>
/// <remarks>
///     <para>
///         <b>Two claims, one key space.</b> With <c>EnableIdempotency()</c> a single
///         <see cref="IIdempotencyStore" /> singleton serves two different mechanisms.
///         <see cref="OutboxDeliveryService" /> claims the row's publish, so that a crash between the
///         publish and the mark does not publish the row twice; <see cref="TransportAwareMessageBus" />
///         claims the delivered message's id before dispatching, so that a redelivery does not run the
///         handlers twice. If both claimed the same string, in a service that consumes what it publishes
///         the second claim would meet the first — made moments earlier, in this same process — and the
///         delivery would be dropped as a duplicate of itself.
///     </para>
///     <para>
///         ⚠️ Nothing would say so anywhere. The row would carry no error, its <c>ProcessedAt</c> would
///         be set, the queue would be bound and end up empty, and the handler would never be entered.
///         The only line about it would be the bus's <c>Debug</c> "duplicate dropped", which no
///         application runs at.
///     </para>
///     <para>
///         A service that handles only what <b>another</b> service published cannot show it: the other
///         service's store knows nothing of this one's claims.
///     </para>
/// </remarks>
#pragma warning disable CA2007 // xUnit manages SynchronizationContext
[Collection("RabbitMqBroker")]
public sealed class AServiceThatHandlesWhatItsOwnOutboxPublishedTests(RabbitMqContainerFixture broker)
{
    /// <summary>
    ///     The whole loop, over a real broker: entity saved → row in the outbox → pump publishes →
    ///     RabbitMQ → this same service's subscription → its own handler.
    /// </summary>
    [Fact]
    public async Task TheServicesOwnHandlerReceivesWhatItsOutboxPublished()
    {
        if (broker.ConnectionString is null)
            return;

        var handled = new Received();

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        await using var sp = BuildService(
            handled,
            o => o.UseSqlite(connection),
            msg => msg.UseRabbitMq(rabbit =>
            {
                rabbit.ConnectionString = broker.ConnectionString!;
                rabbit.DurableQueues = false;
                rabbit.PersistentMessages = false;
            }));

        using (var scope = sp.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CasesDbContext>().Database.EnsureCreatedAsync();

        // Bind before publishing, and bind by awaiting the binder rather than by starting the consumer
        // service and hoping: the exchange discards a message that no queue is bound to, and
        // RabbitMqConsumerService binds inside a BackgroundService whose completion nobody can await.
        // This is the same call it makes, one line further in.
        var transport = sp.GetRequiredService<RabbitMqTransport>();
        await transport.ConnectAsync();
        var handles = await TransportSubscriptionBinder.BindAsync(
            transport,
            sp.GetRequiredService<IMessageRouter>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetServices<MessageSubscription>(),
            logger: sp.GetRequiredService<ILogger<AServiceThatHandlesWhatItsOwnOutboxPublishedTests>>());

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CasesDbContext>();
            var subject = new Case { Id = Guid.NewGuid() };
            subject.Decide("granted");
            db.Cases.Add(subject);
            await db.SaveChangesAsync();
        }

        var pump = sp.GetServices<IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => handled.Count >= 1, TimeSpan.FromSeconds(20));

            // Exactly once: give a second delivery the time it would need to arrive before saying there
            // was none. The dedup this story keeps is what makes this half true, and it has its own
            // control below.
            await Task.Delay(TimeSpan.FromSeconds(1));
            handled.Count.Should().Be(1, "one event was raised, and the handler ran once");
            handled.Last!.Outcome.Should().Be("granted");
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
            foreach (var handle in handles)
                await handle.DisposeAsync();
        }
    }

    /// <summary>
    ///     Control — the consume-side dedup still does its job: the same delivery handed over twice runs
    ///     the handler once. Removing it would make the test above pass and this one fail.
    /// </summary>
    [Fact]
    public async Task ARedeliveryOfTheSameMessageRunsTheHandlerOnce()
    {
        var handled = new Received();

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        await using var sp = BuildService(handled, o => o.UseSqlite(connection), msg => msg.UseChannels());

        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var context = MessageContext.New();
        var message = new TheDecisionWasTaken(Guid.NewGuid(), "granted", DateTimeOffset.UtcNow);

        await bus.DispatchAsync(message, context);
        await bus.DispatchAsync(message, context);

        handled.Count.Should().Be(1, "the transport redelivered one message, it did not carry two");
    }

    /// <summary>
    ///     Control — the publish-side claim still does its job: a row that stays pending across two
    ///     sweeps of the pump is published once, not once per sweep. Deleting that claim instead of
    ///     giving it a key of its own would make the first test pass and this one fail.
    /// </summary>
    /// <remarks>
    ///     The row that never leaves the pending set is the crash the claim exists for: the publish
    ///     succeeded and the process died before the mark, so the next sweep reads it again. Asserted
    ///     through the pump's behaviour and not through the key it claims under, so this stays true
    ///     whatever that key becomes.
    /// </remarks>
    [Fact]
    public async Task ARowThatStaysPendingIsPublishedOnceAndNotOncePerSweep()
    {
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = typeof(TheDecisionWasTaken).FullName!,
            Payload = JsonSerializer.Serialize(new TheDecisionWasTaken(Guid.NewGuid(), "granted", DateTimeOffset.UtcNow)),
            RetryCount = 0,
        };

        var source = new AlwaysPendingSource(row);
        var bus = new CountingBus();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxSource>(source);
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageTypeRegistry>(new TheDecisionRegistry());
        services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore());
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1, MaxRetries = 5 }));

        await using var sp = services.BuildServiceProvider();

        var pump = new OutboxDeliveryService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<OutboxDeliveryService>>());

        await pump.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => source.SweepsServed >= 3, TimeSpan.FromSeconds(10));
        await pump.StopAsync(CancellationToken.None);

        bus.Published.Should().Be(1, "the first sweep published it and the claim answered for the rest");
    }

    /// <summary>
    ///     One service: an EF outbox on its own database, the transport the caller chooses, idempotency
    ///     on — which is what Casework configures — and a handler of its own for what it publishes.
    /// </summary>
    private static ServiceProvider BuildService(
        Received handled,
        Action<DbContextOptionsBuilder> configureDb,
        Action<MessagingBuilder> configureTransport)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<CasesDbContext>((sp, o) =>
        {
            configureDb(o);
            o.AddInterceptors(sp.GetRequiredService<OutboxInterceptor>());
        });

        services.AddSingleton<IMessageTypeRegistry>(new TheDecisionRegistry());
        services.AddPragmaticMessaging(msg =>
        {
            configureTransport(msg);
            msg.EnableOutbox(outbox => outbox.PollingIntervalSeconds = 1);
            msg.EnableIdempotency();
        });

        services.AddScoped<IMessageHandler<TheDecisionWasTaken>>(_ => new TellSomebody(handled));
        // A hand-written subscription says who is listening, because nothing else can: the generator
        // reads the module off the compilation, and the binder refuses a subscription with no
        // subscriber rather than naming the queue after the transport and the message — which is how
        // two services ended up sharing one.
        services.AddMessageSubscription<TheDecisionWasTaken>(subscriber: "cases");
        services.AddMessagingOutbox<CasesDbContext>("Cases");

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Polls until <paramref name="condition" /> holds, and THROWS if it never does — a wait that
    ///     returns quietly reports the symptom of its own timeout as a later assertion.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was still false after {timeout.TotalSeconds:0.#}s.");
    }

    // --- The service being tested -----------------------------------------------------------------

    private sealed class CasesDbContext(DbContextOptions<CasesDbContext> options) : DbContext(options)
    {
        public DbSet<Case> Cases => Set<Case>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Case>(e =>
            {
                e.HasKey(c => c.Id);
                e.Ignore(c => c.DomainEvents);
            });

            modelBuilder.AddMessagingOutbox();
        }
    }

    private sealed class Case : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];

        public Guid Id { get; set; }

        public string Outcome { get; private set; } = "";

        public void Decide(string outcome)
        {
            Outcome = outcome;
            _events.Add(new TheDecisionWasTaken(Id, outcome, DateTimeOffset.UtcNow));
        }

        public IReadOnlyList<IDomainEvent> DomainEvents => _events;

        public void ClearDomainEvents() => _events.Clear();
    }

    /// <summary>
    ///     ⚠️ Public, and it has to be. Without an SG-generated dispatch table — which a test assembly
    ///     does not have — <c>InMemoryMessageBus.DispatchAsync</c> reaches the typed fan-out through the
    ///     DLR, and the DLR binds with the accessibility of <c>Pragmatic.Messaging.Core</c>: a private
    ///     type there is <c>object</c>, <c>IMessageHandler&lt;object&gt;</c> resolves nothing, and the
    ///     dispatch returns having found no handler. That is a test-only trap — an application's message
    ///     types are public and have a dispatch table — but it silently produces exactly the symptom
    ///     this file is about, which is why it is written down here.
    /// </summary>
    public sealed record TheDecisionWasTaken(Guid CaseId, string Outcome, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TellSomebody(Received handled) : IMessageHandler<TheDecisionWasTaken>
    {
        public Task HandleAsync(TheDecisionWasTaken message, MessageContext context, CancellationToken ct)
        {
            handled.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>What the handler was given, across the consume scopes it runs in.</summary>
    private sealed class Received
    {
        private readonly List<TheDecisionWasTaken> _messages = [];

        public int Count
        {
            get
            {
                lock (_messages)
                    return _messages.Count;
            }
        }

        public TheDecisionWasTaken? Last
        {
            get
            {
                lock (_messages)
                    return _messages.Count == 0 ? null : _messages[^1];
            }
        }

        public void Add(TheDecisionWasTaken message)
        {
            lock (_messages)
                _messages.Add(message);
        }
    }

    private sealed class TheDecisionRegistry : IMessageTypeRegistry
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public object? Deserialize(string fullyQualifiedTypeName, string json)
            => fullyQualifiedTypeName == typeof(TheDecisionWasTaken).FullName
                ? JsonSerializer.Deserialize<TheDecisionWasTaken>(json, Options)
                : null;
    }

    // --- Doubles for the publish-side control -----------------------------------------------------

    /// <summary>A row the mark never removes — the crash between the publish and the mark, held open.</summary>
    private sealed class AlwaysPendingSource(OutboxMessage row) : IOutboxSource
    {
        private int _sweeps;

        public string BoundaryName => "Cases";

        public int SweepsServed => Volatile.Read(ref _sweeps);

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _sweeps);
            return Task.FromResult<IReadOnlyList<OutboxMessage>>([row]);
        }

        public Task MarkProcessedAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> PurgeProcessedAsync(TimeSpan retention, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class CountingBus : IMessageBus
    {
        public int Published { get; private set; }

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            Published++;
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull
            => throw new NotSupportedException();

        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
