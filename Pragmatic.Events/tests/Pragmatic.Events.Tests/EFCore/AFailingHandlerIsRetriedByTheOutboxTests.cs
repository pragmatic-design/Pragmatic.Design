using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Outbox;
using Pragmatic.Events.Tests.Fixtures;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     A handler that fails is the entry's failed attempt, through the real dispatcher.
/// </summary>
/// <remarks>
///     ⚠️ <c>EventOutboxDrainerTests</c> covers a failing delivery with a stub dispatcher that throws, and the
///     drainer did count that. The dispatcher the application gets does not throw: <c>InMemoryEventDispatcher</c>
///     isolates each handler, logs its failure and goes on, so the drainer saw a delivery that returned normally,
///     marked the entry processed, and a handler that failed once never ran again.
/// </remarks>
public sealed class AFailingHandlerIsRetriedByTheOutboxTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = PragmaticJsonOptions.Default.Build();

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FailingHandler _handler = new();

    public AFailingHandlerIsRetriedByTheOutboxTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IDomainEventHandler<TestDomainEvent>>(_handler);
        services.AddScoped<IDomainEventDispatcher, InMemoryEventDispatcher>();
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(TestDomainEvent)]));
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task AHandlerThatFailsOnce_RunsAgainOnTheNextDrain_AndOnlyThenIsTheEntryDelivered()
    {
        _handler.FailuresLeft = 1;
        var id = SeedEntry(new TestDomainEvent("once"));

        await CreateDrainer(maxAttempts: 5).DrainOnceAsync();

        var afterFailure = Reload(id);
        afterFailure.ProcessedAt.Should().BeNull("the handler failed, so the event was not delivered");
        afterFailure.Attempts.Should().Be(1);
        afterFailure.LastError.Should().Contain("handler failed on purpose");

        await CreateDrainer(maxAttempts: 5).DrainOnceAsync();

        _handler.Calls.Should().Be(2, "the second drain runs the handler again");
        Reload(id).ProcessedAt.Should().NotBeNull("delivered only once the handler succeeded");
    }

    [Fact]
    public async Task AHandlerThatKeepsFailing_StopsAfterTheConfiguredAttempts_AndIsRecordedAsFailed()
    {
        _handler.FailuresLeft = int.MaxValue;
        var id = SeedEntry(new TestDomainEvent("always"));

        for (var pass = 0; pass < 4; pass++)
            await CreateDrainer(maxAttempts: 3).DrainOnceAsync();

        _handler.Calls.Should().Be(3, "the fourth drain finds the entry past its attempts and leaves it");
        var entry = Reload(id);
        entry.ProcessedAt.Should().BeNull();
        entry.Attempts.Should().Be(3);
        entry.LastError.Should().Contain("handler failed on purpose", "the failure is kept on the entry");
        entry.ClaimedBy.Should().BeNull();
    }

    /// <summary>The control: a handler that succeeds is delivered on the first drain, as before.</summary>
    [Fact]
    public async Task AHandlerThatSucceeds_IsDeliveredOnTheFirstDrain()
    {
        var id = SeedEntry(new TestDomainEvent("fine"));

        await CreateDrainer(maxAttempts: 5).DrainOnceAsync();

        _handler.Calls.Should().Be(1);
        var entry = Reload(id);
        entry.ProcessedAt.Should().NotBeNull();
        entry.Attempts.Should().Be(0);
    }

    private EventOutboxDrainer<OutboxTestDbContext> CreateDrainer(int maxAttempts)
        => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new EventOutboxOptions { MaxAttempts = maxAttempts },
            NullLogger<EventOutboxDrainer<OutboxTestDbContext>>.Instance);

    private Guid SeedEntry(TestDomainEvent domainEvent)
    {
        using var scope = _provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        var entry = new EventOutboxEntry
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestDomainEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, JsonOptions.GetTypeInfo(typeof(TestDomainEvent))),
            OccurredAt = domainEvent.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow,
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

    private sealed class FailingHandler : IDomainEventHandler<TestDomainEvent>
    {
        public int FailuresLeft { get; set; }
        public int Calls { get; private set; }

        public Task HandleAsync(TestDomainEvent @event, CancellationToken ct = default)
        {
            Calls++;
            if (FailuresLeft <= 0)
                return Task.CompletedTask;

            FailuresLeft--;
            throw new InvalidOperationException("handler failed on purpose");
        }
    }
}
