using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     End-to-end outbox test: entity save → OutboxInterceptor writes to outbox →
///     read pending → deserialize → dispatch → handler receives.
/// </summary>
#pragma warning disable CA2007
public class OutboxEndToEndTests
{
    public record TestEvent(string Data, DateTimeOffset OccurredAt) : IDomainEvent;

    public class TestEntity : IHasDomainEvents
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        private readonly List<IDomainEvent> _events = [];
        public IReadOnlyList<IDomainEvent> DomainEvents => _events;
        public void ClearDomainEvents() => _events.Clear();
        public void RaiseEvent(IDomainEvent @event) => _events.Add(@event);
    }

    public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
            new OutboxEntityTypeConfiguration().Configure(modelBuilder.Entity<OutboxMessage>());
        }
    }

    [Fact]
    public async Task OutboxInterceptor_ShouldWriteEventsToOutboxTable()
    {
        var dbName = $"outbox-write-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new OutboxInterceptor())
            .Options;

        await using var db = new TestDbContext(options);

        var entity = new TestEntity { Name = "Test" };
        entity.RaiseEvent(new TestEvent("step1", DateTimeOffset.UtcNow));
        entity.RaiseEvent(new TestEvent("step2", DateTimeOffset.UtcNow));

        db.Entities.Add(entity);
        await db.SaveChangesAsync();

        // Outbox should have 2 messages
        var outboxMessages = await db.OutboxMessages.ToListAsync();
        outboxMessages.Should().HaveCount(2);
        outboxMessages[0].MessageType.Should().Contain("TestEvent");
        outboxMessages[0].Payload.Should().Contain("step1");
        outboxMessages[1].Payload.Should().Contain("step2");
        outboxMessages.Should().OnlyContain(m => m.ProcessedAt == null);

        // Entity events should be cleared
        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task FullOutboxFlow_SaveDispatchReceive()
    {
        var dbName = $"outbox-e2e-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticMessaging();

        var receivedMessages = new List<string>();
        services.AddScoped<IMessageHandler<TestEvent>>(_ => new CapturingHandler(receivedMessages));

        services.AddDbContext<TestDbContext>((_, opt) =>
        {
            opt.UseInMemoryDatabase(dbName);
            opt.AddInterceptors(new OutboxInterceptor());
        });

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Step 1: Save entity with domain event → OutboxInterceptor writes to outbox
        var entity = new TestEntity { Name = "E2E" };
        entity.RaiseEvent(new TestEvent("outbox-flow", DateTimeOffset.UtcNow));
        db.Entities.Add(entity);
        await db.SaveChangesAsync();

        // Step 2: Read pending outbox messages
        var pending = await db.OutboxMessages.Where(m => m.ProcessedAt == null).ToListAsync();
        pending.Should().ContainSingle();

        var outboxMsg = pending[0];
        outboxMsg.Payload.Should().Contain("outbox-flow");

        // Step 3: Deserialize and dispatch (simulates OutboxDeliveryService)
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<TestEvent>(outboxMsg.Payload,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        deserialized.Should().NotBeNull();

        await bus.DispatchAsync(deserialized!, MessageContext.New());

        // Step 4: Mark as processed
        outboxMsg.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Assert: handler received the message
        receivedMessages.Should().ContainSingle().Which.Should().Be("outbox-flow");

        // Assert: outbox message is marked processed
        var processed = await db.OutboxMessages.SingleAsync();
        processed.ProcessedAt.Should().NotBeNull();
    }

    private class CapturingHandler(List<string> captured) : IMessageHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent message, MessageContext context, CancellationToken ct)
        {
            captured.Add(message.Data);
            return Task.CompletedTask;
        }
    }
}
