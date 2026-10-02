using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Events;
using Pragmatic.Messaging.Entities;

namespace Pragmatic.Messaging.Tests.EFCore;

public class OutboxInterceptorTests
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

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new OutboxInterceptor())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChanges_WithDomainEvents_ShouldCreateOutboxMessages()
    {
        await using var ctx = CreateContext();
        var entity = new TestEntity { Name = "Test" };
        entity.RaiseEvent(new TestEvent("data-1", DateTimeOffset.UtcNow));
        entity.RaiseEvent(new TestEvent("data-2", DateTimeOffset.UtcNow));

        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        var outboxMessages = await ctx.OutboxMessages.ToListAsync();
        outboxMessages.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveChanges_OutboxMessage_ShouldContainSerializedPayload()
    {
        await using var ctx = CreateContext();
        var entity = new TestEntity { Name = "Test" };
        entity.RaiseEvent(new TestEvent("hello", DateTimeOffset.UtcNow));

        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        var outboxMsg = await ctx.OutboxMessages.SingleAsync();
        outboxMsg.Payload.Should().Contain("hello");
        outboxMsg.MessageType.Should().Contain("TestEvent");
        outboxMsg.ProcessedAt.Should().BeNull();
        outboxMsg.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveChanges_ShouldClearEventsFromEntity()
    {
        await using var ctx = CreateContext();
        var entity = new TestEntity { Name = "Test" };
        entity.RaiseEvent(new TestEvent("data", DateTimeOffset.UtcNow));

        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChanges_WithoutEvents_ShouldNotCreateOutboxMessages()
    {
        await using var ctx = CreateContext();
        var entity = new TestEntity { Name = "Test" };

        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        var count = await ctx.OutboxMessages.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task SaveChanges_MultipleEntitiesWithEvents_ShouldCollectAll()
    {
        await using var ctx = CreateContext();
        var entity1 = new TestEntity { Name = "E1" };
        var entity2 = new TestEntity { Name = "E2" };
        entity1.RaiseEvent(new TestEvent("from-e1", DateTimeOffset.UtcNow));
        entity2.RaiseEvent(new TestEvent("from-e2-a", DateTimeOffset.UtcNow));
        entity2.RaiseEvent(new TestEvent("from-e2-b", DateTimeOffset.UtcNow));

        ctx.Entities.AddRange(entity1, entity2);
        await ctx.SaveChangesAsync();

        var count = await ctx.OutboxMessages.CountAsync();
        count.Should().Be(3);
    }

    [Fact]
    public async Task SaveChanges_OutboxMessage_ShouldHaveUniqueIds()
    {
        await using var ctx = CreateContext();
        var entity = new TestEntity { Name = "Test" };
        entity.RaiseEvent(new TestEvent("a", DateTimeOffset.UtcNow));
        entity.RaiseEvent(new TestEvent("b", DateTimeOffset.UtcNow));

        ctx.Entities.Add(entity);
        await ctx.SaveChangesAsync();

        var messages = await ctx.OutboxMessages.ToListAsync();
        messages.Select(m => m.Id).Should().OnlyHaveUniqueItems();
    }
}
