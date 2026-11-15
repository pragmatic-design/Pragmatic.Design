using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Outbox;
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

    /// <summary>
    ///     The events an operation declared, staged by the invocation that owns the commit, are written in the same save, built there, and
    ///     taken: the invoker finds nothing left to dispatch after the commit (#736).
    /// </summary>
    [Fact]
    public void SavingChanges_DeclaredEventsStaged_WritesThemBesideTheEntitysAndEmptiesTheStage()
    {
        using var services = new ServiceCollection()
            .AddScoped<DeclaredEventStage>()
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        var stage = scope.ServiceProvider.GetRequiredService<DeclaredEventStage>();
        using var context = new OutboxTestDbContext(new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(scope.ServiceProvider)
            .AddInterceptors(new EventOutboxInterceptor())
            .Options);
        var built = 0;
        stage.Stage(() =>
        {
            built++;
            return [new TestDomainEvent("Declared by the operation")];
        });

        context.Entities.Add(new TestDbEntity { Name = "NoEvents" });
        context.SaveChanges();

        var row = context.Set<EventOutboxEntry>().ToList().Should().ContainSingle().Subject;
        row.Payload.Should().Contain("Declared by the operation");
        built.Should().Be(1, "the events are built in the save, once");
        stage.Take().Should().BeEmpty("the outbox took them: the invoker dispatches none after the commit");
    }

    /// <summary>The control: without a stage in the container, a save with no entity events writes nothing, as before.</summary>
    [Fact]
    public void SavingChanges_NoStageRegistered_WritesNoOutboxRow()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var context = new OutboxTestDbContext(new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseApplicationServiceProvider(services)
            .AddInterceptors(new EventOutboxInterceptor())
            .Options);

        context.Entities.Add(new TestDbEntity { Name = "NoEvents" });
        context.SaveChanges();

        context.Set<EventOutboxEntry>().Should().BeEmpty();
    }

    private static OutboxTestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<OutboxTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EventOutboxInterceptor())
            .Options);
}
