using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Tests.Fixtures;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pragmatic.Events.Tests.Integration;

/// <summary>
///     End-to-end test of the transactional outbox against a real PostgreSQL database (the production
///     target), exercising the full cycle: the interceptor captures the domain event into
///     <c>__EventOutbox</c> in the same transaction as the entity change, then the delivery service
///     atomically claims the row (<c>ExecuteUpdate</c> CAS), dispatches it through the real
///     <c>InMemoryEventDispatcher</c> to a registered handler, and marks it processed.
/// </summary>
/// <remarks>
///     Standalone (does not wire into the showcase host, whose generated DbContexts have no seam to
///     enable the outbox — tracked as EV-M8). Requires Docker; runs where Testcontainers can start.
/// </remarks>
public sealed class EventOutboxPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private ServiceProvider _provider = null!;

    // Static sink so the handler stays parameterless — the obsolete assembly-scan test activates every
    // IDomainEventHandler in this assembly, so a constructor dependency here would break that test.
    private static readonly List<IDomainEvent> Received = [];

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        Received.Clear();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxTestDbContext>(o => o
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(new EventOutboxInterceptor()));

        // Real dispatch path: InMemoryEventDispatcher resolves the registered handler.
        services.AddInMemoryDomainEvents();
        services.AddScoped<IDomainEventHandler<TestDomainEvent>, RecordingHandler>();
        services.AddSingleton<IEventOutboxTypeResolver>(new EventOutboxTypeResolver([typeof(TestDomainEvent)]));

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database
            .EnsureCreatedAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Outbox_CaptureThenDeliver_FullCycleOnPostgres()
    {
        // Act 1 — capture: a change that raises a domain event writes an outbox row in the same transaction.
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            var entity = new TestDbEntity { Name = "Initial" };
            context.Entities.Add(entity);
            await context.SaveChangesAsync();

            entity.ChangeName("Confirmed");
            await context.SaveChangesAsync();
        }

        using (var verify = _provider.CreateScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            (await context.Set<EventOutboxEntry>().CountAsync(e => e.ProcessedAt == null))
                .Should().Be(1, "the interceptor persisted the event to the outbox");
        }

        // Act 2 — deliver: one pass claims and dispatches the pending entry.
        var delivery = new EventOutboxDrainer<OutboxTestDbContext>(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new EventOutboxOptions(),
            NullLogger<EventOutboxDrainer<OutboxTestDbContext>>.Instance);

        await delivery.DrainOnceAsync(CancellationToken.None);

        // Assert — the handler ran and the row is marked processed.
        Received.Should().ContainSingle()
            .Which.Should().BeOfType<TestDomainEvent>()
            .Which.Message.Should().Contain("Confirmed");

        using var scope2 = _provider.CreateScope();
        var entry = await scope2.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<EventOutboxEntry>().AsNoTracking().SingleAsync();
        entry.ProcessedAt.Should().NotBeNull("a delivered outbox entry is marked processed");
        entry.Attempts.Should().Be(0);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class RecordingHandler : IDomainEventHandler<TestDomainEvent>
    {
        public Task HandleAsync(TestDomainEvent domainEvent, CancellationToken ct = default)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
