using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     One delivery pass — <see cref="EventOutboxDrainer{TContext}" /> — exercised over a real
///     relational provider (SQLite in-memory) because the claim uses <c>ExecuteUpdate</c>, which
///     the EF InMemory provider does not support.
/// </summary>
/// <remarks>
///     The pass is what the background loop calls on a timer and what an application calls
///     directly, so this covers both. The loop itself is a <c>while</c> and a <c>Task.Delay</c>
///     around it, and asserting on that would be asserting the scheduler.
/// </remarks>
public sealed class EventOutboxDrainerTests : IDisposable
{
    // Same AOT-safe options the drainer uses to deserialize, so seeded payloads round-trip.
    private static readonly JsonSerializerOptions JsonOptions = PragmaticJsonOptions.Default.Build();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly RecordingDispatcher _dispatcher = new();

    public EventOutboxDrainerTests()
    {
        // A single shared open connection keeps the in-memory DB alive across scopes/contexts.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IDomainEventDispatcher>(_dispatcher);
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(TestDomainEvent)]));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task DrainOnce_KnownEventWithHandler_DispatchesAndMarksProcessed()
    {
        var id = SeedEntry(new TestDomainEvent("hello"));

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _dispatcher.Dispatched.Should().ContainSingle()
            .Which.Should().BeOfType<TestDomainEvent>()
            .Which.Message.Should().Be("hello");

        var entry = Reload(id);
        entry.ProcessedAt.Should().NotBeNull("a delivered entry is marked processed");
        entry.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task DrainOnce_UnknownEventType_FailsClosed_NotDispatched()
    {
        // Resolver only allows TestDomainEvent; seed an entry whose type is not allowlisted.
        var id = SeedEntry(new AnotherTestEvent(1), eventType: typeof(AnotherTestEvent).AssemblyQualifiedName!);

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _dispatcher.Dispatched.Should().BeEmpty("a disallowed type must never be deserialized or dispatched");

        var entry = Reload(id);
        entry.ProcessedAt.Should().BeNull();
        entry.Attempts.Should().Be(1);
        entry.LastError.Should().Contain("No registered handler");
        entry.ClaimedBy.Should().BeNull("the claim is released so the entry can be retried");
    }

    [Fact]
    public async Task DrainOnce_AttemptsAtMaxAttempts_IsNotSelected()
    {
        var id = SeedEntry(new TestDomainEvent("poison"), attempts: 5);

        // Options MaxAttempts default = 5, so Attempts == 5 is beyond the ceiling and ineligible.
        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _dispatcher.Dispatched.Should().BeEmpty();
        var entry = Reload(id);
        entry.ProcessedAt.Should().BeNull("poison entries beyond MaxAttempts are abandoned, not retried");
        entry.Attempts.Should().Be(5, "an ineligible entry must not be touched");
    }

    [Fact]
    public async Task DrainOnce_ClaimedByAnotherWorker_IsNotGrabbed()
    {
        var id = SeedEntry(new TestDomainEvent("claimed"),
            claimedBy: "other-worker",
            claimedUntil: DateTimeOffset.UtcNow.AddMinutes(10));

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _dispatcher.Dispatched.Should().BeEmpty("a live claim by another worker blocks delivery");
        var entry = Reload(id);
        entry.ProcessedAt.Should().BeNull();
        entry.ClaimedBy.Should().Be("other-worker", "the foreign claim must be left intact");
    }

    [Fact]
    public async Task DrainOnce_ExpiredClaim_IsReGrabbedAndDelivered()
    {
        var id = SeedEntry(new TestDomainEvent("expired"),
            claimedBy: "crashed-worker",
            claimedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        _dispatcher.Dispatched.Should().ContainSingle("an expired claim is re-grabbable (crash recovery)");
        Reload(id).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DrainOnce_HandlerThrows_ReleasesClaimAndRecordsError()
    {
        _dispatcher.Throw = true;
        var id = SeedEntry(new TestDomainEvent("boom"));

        await CreateDrainer().DrainOnceAsync(CancellationToken.None);

        var entry = Reload(id);
        entry.ProcessedAt.Should().BeNull();
        entry.Attempts.Should().Be(1);
        entry.LastError.Should().NotBeNullOrEmpty();
        entry.ClaimedBy.Should().BeNull("a failed delivery releases the claim for a later retry");
    }

    private EventOutboxDrainer<OutboxTestDbContext> CreateDrainer()
        => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new EventOutboxOptions(),
            NullLogger<EventOutboxDrainer<OutboxTestDbContext>>.Instance);

    private Guid SeedEntry(
        IDomainEvent domainEvent,
        string? eventType = null,
        int attempts = 0,
        string? claimedBy = null,
        DateTimeOffset? claimedUntil = null)
    {
        using var scope = _provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        var entry = new EventOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = eventType ?? domainEvent.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, JsonOptions.GetTypeInfo(domainEvent.GetType())),
            OccurredAt = domainEvent.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = attempts,
            ClaimedBy = claimedBy,
            ClaimedUntil = claimedUntil,
        };
        ctx.Add(entry);
        ctx.SaveChanges();
        return entry.Id;
    }

    private EventOutboxEntry Reload(Guid id)
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<EventOutboxEntry>().AsNoTracking().Single(e => e.Id == id);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Dispatched { get; } = [];
        public bool Throw { get; set; }

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
        {
            if (Throw) throw new InvalidOperationException("handler failed");
            Dispatched.Add(@event);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("handler failed");
            Dispatched.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
