using Microsoft.EntityFrameworkCore;
using Pragmatic.Events;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Events raised by an entity written through a repository are handed to the batch, not dispatched.
/// </summary>
/// <remarks>
///     <para>
///         The unit of work is the only place domain events leave an entity. An interceptor doing it
///         too, on <c>SavedChangesAsync</c> — inside <c>SaveChanges</c>, from a scope of its own — would
///         not agree with it, because the lifecycle events are raised in between. And what an
///         interceptor dispatches, it dispatches in a fresh scope where no tenant is resolved, so every
///         fail-closed filter hides the rows the handler has been called to act on.
///     </para>
///     <para>
///         Taken after the save and only on success, then handed to whoever owns the commit — they flush
///         it outside their own claim — or dispatched here when nobody does. The hand-over stays
///         <b>narrow</b>: a batch belonging to another unit of work is not ours to fill, and with no
///         owner and no dispatcher the events are left exactly where they were rather than destroyed.
///     </para>
/// </remarks>
public sealed class EfCoreUnitOfWorkDeferredEventsTests
{
    // SQLite :memory: lives as long as the open connection the DbContext holds.
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static TestAnnouncement Published(TestDbContext db)
    {
        var announcement = new TestAnnouncement { PersistenceId = Guid.NewGuid(), Headline = "Doors open" };
        announcement.Publish();
        db.Add(announcement);
        return announcement;
    }

    /// <summary>
    ///     The hand-over: the batch holds the event and the entity no longer does.
    /// </summary>
    /// <remarks>
    ///     Both halves are the assertion. The batch holding it is what gets it dispatched after the
    ///     commit; the entity being empty is what stops the interceptor from dispatching it before.
    /// </remarks>
    [Fact]
    public async Task WithACoveringBatch_TheEventsGoToTheBatch()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);
        using var batch = new BatchContext(uow, defersSave: false);

        var announcement = Published(db);
        await uow.SaveChangesAsync();

        batch.DeferredEvents.Should().HaveCount(1, "the batch flushes them after the commit");
        announcement.DomainEvents.Should().BeEmpty(
            "what the interceptor still finds on the entity is what it dispatches mid-save");
    }

    /// <summary>
    ///     Control: with no batch the unit of work takes nothing, and the interceptor stays the owner.
    /// </summary>
    /// <remarks>
    ///     Without this the change would look correct while having quietly swallowed every event of
    ///     every plain save in the framework — there is no batch outside a composition.
    /// </remarks>
    [Fact]
    public async Task WithoutABatch_TheEventsAreLeftOnTheEntity()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var announcement = Published(db);
        await uow.SaveChangesAsync();

        announcement.DomainEvents.Should().HaveCount(1,
            "no owner and no dispatcher, so there is nowhere to put them — and clearing them would "
            + "destroy them without a word");
    }

    /// <summary>
    ///     Control: a batch naming another unit of work is not ours to put events in.
    /// </summary>
    /// <remarks>
    ///     It is the rule <c>CommitScope.CoveringBatch</c> already applies to commits, and getting it
    ///     wrong here would park a boundary's events in another boundary's flush — dispatched late if
    ///     that one commits, and lost if it does not.
    /// </remarks>
    [Fact]
    public async Task WithABatchOfAnotherUnitOfWork_TheEventsAreLeftAlone()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);
        IUnitOfWork elsewhere = new EfCoreUnitOfWork(db);
        using var batch = new BatchContext(elsewhere, defersSave: false);

        var announcement = Published(db);
        await uow.SaveChangesAsync();

        batch.DeferredEvents.Should().BeEmpty();
        announcement.DomainEvents.Should().HaveCount(1);
    }

    /// <summary>
    ///     With no owner but a dispatcher registered, the unit of work dispatches them itself.
    /// </summary>
    /// <remarks>
    ///     This is the path a plain repository write takes — no invoker above it, nobody to hand the
    ///     events to. It happens in the caller's scope, not in an interceptor's scope of its own, which
    ///     is what gives a handler the tenant and the user of the request that caused the write.
    /// </remarks>
    [Fact]
    public async Task WithoutABatchButWithADispatcher_TheUnitOfWorkDispatchesThem()
    {
        using var db = CreateContext();
        var dispatcher = new RecordingDispatcher();
        IUnitOfWork uow = new EfCoreUnitOfWork(db, logger: null, dispatcher);

        var announcement = Published(db);
        await uow.SaveChangesAsync();

        dispatcher.Dispatched.Should().HaveCount(1);
        announcement.DomainEvents.Should().BeEmpty("dispatched, so no longer pending on the entity");
    }

    /// <summary>
    ///     A save that failed hands over nothing: no event for a row that was refused.
    /// </summary>
    /// <remarks>
    ///     The events are taken after the save and only on success, so a refused row never reaches the
    ///     batch: announcing a write that never happened is worse than losing the announcement of one
    ///     that did. They are also not cleared off the refused entity — nothing was taken from it —
    ///     which is what lets a retry still announce the write if it lands.
    /// </remarks>
    [Fact]
    public async Task WhenTheSaveFails_NothingIsHandedOver()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);
        using var batch = new BatchContext(uow, defersSave: false);

        var first = Published(db);
        await uow.SaveChangesAsync();
        uow.Detach(first);

        // Same key, so the clash happens at the database rather than in the change tracker.
        var duplicate = new TestAnnouncement { PersistenceId = first.PersistenceId, Headline = "Again" };
        duplicate.Publish();
        db.Add(duplicate);
        await Assert.ThrowsAnyAsync<Exception>(() => uow.SaveChangesAsync());

        batch.DeferredEvents.Should().HaveCount(1,
            "the first row was written and the second was not, so only one event survives");
        duplicate.DomainEvents.Should().HaveCount(1,
            "never taken, because the save that would have justified announcing them failed");
    }

    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Dispatched { get; } = [];

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : IDomainEvent
        {
            Dispatched.Add(@event);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            Dispatched.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
