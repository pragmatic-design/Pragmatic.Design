using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     What an <see cref="ICommitObserver" /> sees on EF Core, through <see cref="CommitObserverInterceptor" />: the entries before the save
///     writes them, and once their commit succeeded — at the save when it ran in a transaction of its own, at the commit of the caller's
///     otherwise, never after a rollback, and a rollback to a savepoint forgetting the entries after it. Over SQLite: the InMemory provider has
///     no transactions.
/// </summary>
public sealed class CommitObserverInterceptorTests
{
    private static TestDbContext CreateContext(ICommitObserver observer)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(CommitObserverInterceptor.For([observer]))
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static TestCustomer Customer(string name) => new() { FullName = name, Email = $"{name}@test.dev" };

    [Fact]
    public void No_observer_registers_no_interceptor()
        => CommitObserverInterceptor.For([]).Should().BeEmpty();

    [Fact]
    public async Task A_save_on_its_own_is_reported_written_then_committed()
    {
        var seen = new Seen();
        using var db = CreateContext(seen);
        var customer = Customer("alone");

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        seen.WritingEntries().Should().Equal([(customer, CommitEntryState.Added)]);
        seen.CommittedEntries().Should().Equal([(customer, CommitEntryState.Added)]);
    }

    [Fact]
    public async Task A_save_in_a_transaction_is_committed_at_its_commit_and_never_after_a_rollback()
    {
        var seen = new Seen();
        using var db = CreateContext(seen);
        IUnitOfWork uow = new EfCoreUnitOfWork(db);
        var kept = Customer("kept");

        var tx = await uow.BeginTransactionAsync();
        db.Customers.Add(kept);
        await uow.SaveChangesAsync();
        seen.Committed.Should().BeEmpty("written, not committed yet");
        await tx.CommitAsync();

        var undone = await uow.BeginTransactionAsync();
        db.Customers.Add(Customer("undone"));
        await uow.SaveChangesAsync();
        await undone.RollbackAsync();

        seen.CommittedEntries().Should().Equal([(kept, CommitEntryState.Added)]);
    }

    [Fact]
    public async Task A_rollback_to_a_savepoint_forgets_the_entries_saved_after_it()
    {
        var seen = new Seen();
        using var db = CreateContext(seen);
        IUnitOfWork uow = new EfCoreUnitOfWork(db);
        var before = Customer("before");
        var after = Customer("after");

        var tx = await uow.BeginTransactionAsync();
        db.Customers.Add(before);
        await uow.SaveChangesAsync();
        await uow.SavepointAsync("point");
        db.Customers.Add(after);
        await uow.SaveChangesAsync();
        await uow.RollbackToSavepointAsync("point");
        await tx.CommitAsync();

        seen.CommittedEntries().Should().Equal([(before, CommitEntryState.Added)]);
    }

    [Fact]
    public async Task Throwing_before_the_save_writes_nothing()
    {
        using var db = CreateContext(new Refusing());

        db.Customers.Add(Customer("refused"));
        var act = async () => await db.SaveChangesAsync().ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        (await db.Customers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Throwing_after_the_commit_reaches_the_caller_with_the_row_there()
    {
        using var db = CreateContext(new LosingTheAcknowledgement());

        db.Customers.Add(Customer("committed"));
        var act = async () => await db.SaveChangesAsync().ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        (await db.Customers.CountAsync()).Should().Be(1);
    }

    private sealed class Seen : ICommitObserver
    {
        public List<IReadOnlyList<CommitEntry>> Writing { get; } = [];

        public List<IReadOnlyList<CommitEntry>> Committed { get; } = [];

        void ICommitObserver.Writing(IReadOnlyList<CommitEntry> entries) => Writing.Add([.. entries]);

        void ICommitObserver.Committed(IReadOnlyList<CommitEntry> entries) => Committed.Add([.. entries]);

        public List<(object, CommitEntryState)> WritingEntries() => [.. Writing.SelectMany(e => e).Select(e => (e.Entity, e.State))];

        public List<(object, CommitEntryState)> CommittedEntries() => [.. Committed.SelectMany(e => e).Select(e => (e.Entity, e.State))];
    }

    private sealed class Refusing : ICommitObserver
    {
        public void Writing(IReadOnlyList<CommitEntry> entries) => throw new InvalidOperationException("refused");

        public void Committed(IReadOnlyList<CommitEntry> entries)
        {
        }
    }

    private sealed class LosingTheAcknowledgement : ICommitObserver
    {
        public void Writing(IReadOnlyList<CommitEntry> entries)
        {
        }

        public void Committed(IReadOnlyList<CommitEntry> entries) => throw new InvalidOperationException("acknowledgement lost");
    }
}
