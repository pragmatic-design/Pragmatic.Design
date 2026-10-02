using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     Capture behaviour of <see cref="EventOutboxInterceptor" />: during SaveChanges it writes one
///     <c>__EventOutbox</c> row per pending domain event (in the same transaction) and clears the
///     entity's events so they are not also dispatched post-commit.
/// </summary>
public sealed class EventOutboxInterceptorTests
{
    [Fact]
    public void SavingChanges_EntityWithEvents_WritesOutboxRowAndClearsEntityEvents()
    {
        using var context = CreateContext();
        var entity = new TestDbEntity { Name = "Initial" };
        context.Entities.Add(entity);
        context.SaveChanges();

        entity.ChangeName("Updated");
        context.SaveChanges();

        var rows = context.Set<EventOutboxEntry>().ToList();
        rows.Should().ContainSingle();
        rows[0].EventType.Should().Be(typeof(TestDomainEvent).AssemblyQualifiedName);
        rows[0].Payload.Should().Contain("Name changed to Updated");
        rows[0].ProcessedAt.Should().BeNull("a freshly captured entry is pending");
        entity.DomainEvents.Should().BeEmpty("captured events are cleared to avoid double dispatch");
    }

    [Fact]
    public void SavingChanges_NoEvents_WritesNoOutboxRow()
    {
        using var context = CreateContext();
        context.Entities.Add(new TestDbEntity { Name = "NoEvents" });
        context.SaveChanges();

        context.Set<EventOutboxEntry>().Should().BeEmpty();
    }

    [Fact]
    public void SavingChanges_MultipleEvents_WritesOneRowPerEvent()
    {
        using var context = CreateContext();
        var entity = new TestDbEntity { Name = "Initial" };
        context.Entities.Add(entity);
        context.SaveChanges();

        entity.ChangeName("First");
        entity.ChangeName("Second");
        context.SaveChanges();

        context.Set<EventOutboxEntry>().Should().HaveCount(2);
    }

    private static OutboxTestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EventOutboxInterceptor())
            .Options);
}
