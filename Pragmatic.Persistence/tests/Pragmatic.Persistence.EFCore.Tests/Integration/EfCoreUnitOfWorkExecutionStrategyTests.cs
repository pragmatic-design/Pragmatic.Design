using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.EFCore.RollUp;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.RollUp;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The unit of work under an execution strategy that retries — which a generated host now configures
///     for every server provider.
/// </summary>
/// <remarks>
///     <para>
///         A retrying strategy refuses a transaction opened outside it: EF throws as soon as a command
///         meets one. So everything that opens a transaction has to run inside the strategy, and a unit
///         re-run by it has to start from what the database holds, not from what the failed attempt left
///         in the change tracker.
///     </para>
///     <para>
///         SQLite has no retrying strategy of its own, so this one is written here, retrying once on a
///         failure the test raises. What it proves is the unit of work's side of the contract; that a
///         real dropped connection is classified as transient is Npgsql's, and is exercised by
///         Conformance's <c>TheDatabaseRestarts</c>.
///     </para>
/// </remarks>
public sealed class EfCoreUnitOfWorkExecutionStrategyTests
{
    private sealed class Invoice
    {
        public Guid Id { get; set; }
        public decimal Subtotal { get; private set; }
        public void ApplyDelta(decimal delta) => Subtotal += delta;
    }

    private sealed class Line
    {
        public Guid Id { get; set; }
        public Guid InvoiceId { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class StrategyDbContext(DbContextOptions<StrategyDbContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Line> Lines => Set<Line>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Invoice>().HasKey(i => i.Id);
            builder.Entity<Invoice>().Property(i => i.Subtotal).HasPrecision(18, 2);
            builder.Entity<Line>().HasKey(l => l.Id);
            builder.Entity<Line>().Property(l => l.Amount).HasPrecision(18, 2);
        }
    }

    private sealed class SimulatedTransientFailure() : Exception("A transient failure, raised by the test.");

    private sealed class RetriesOnce(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SimulatedTransientFailure;
    }

    private static RollUpRule<Line, Invoice> Subtotal() => new()
    {
        AggregatePropertyName = nameof(Invoice.Subtotal),
        ParentKey = l => l.InvoiceId,
        Amount = l => l.Amount,
        ApplyToParent = (invoice, delta) => invoice.ApplyDelta(delta)
    };

    private static StrategyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<StrategyDbContext>()
            .UseSqlite(connection, sqlite => sqlite.ExecutionStrategy(d => new RetriesOnce(d)))
            .AddInterceptors(new RollUpInterceptor([Subtotal()]))
            .Options);

    // SQLite :memory: lives as long as this connection stays open.
    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.EnsureCreated();
        return connection;
    }

    /// <summary>The control: the strategy this suite configures does retry, and so refuses an outside transaction.</summary>
    [Fact]
    public async Task ATransactionOpenedOutsideTheStrategy_IsRefused()
    {
        using var connection = OpenDatabase();
        using var db = CreateContext(connection);
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();

        using var transaction = await uow.BeginTransactionAsync().ConfigureAwait(true);
        db.Invoices.Add(new Invoice { Id = Guid.NewGuid() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ReRunsTheUnit_FromAClearedChangeTracker()
    {
        using var connection = OpenDatabase();
        using var db = CreateContext(connection);
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var trackedAtStart = new List<int>();
        var invoiceIds = new List<Guid>();

        await uow.ExecuteAsync(async ct =>
        {
            trackedAtStart.Add(db.ChangeTracker.Entries().Count());

            using var transaction = await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
            var invoice = new Invoice { Id = Guid.NewGuid() };
            invoiceIds.Add(invoice.Id);
            db.Invoices.Add(invoice);
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);

            if (invoiceIds.Count == 1)
                throw new SimulatedTransientFailure();

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return invoice.Id;
        });

        trackedAtStart.Should().Equal([0, 0],
            "the second attempt starts from the database, not from the entity the failed one left tracked");
        (await db.Invoices.Select(i => i.Id).ToListAsync()).Should().Equal([invoiceIds[1]],
            "the first attempt's row was rolled back with its transaction");
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheUnitOnce_WhenNothingFails()
    {
        using var connection = OpenDatabase();
        using var db = CreateContext(connection);
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var runs = 0;
        await uow.ExecuteAsync(async ct =>
        {
            runs++;
            using var transaction = await uow.BeginTransactionAsync(ct).ConfigureAwait(false);
            db.Invoices.Add(new Invoice { Id = Guid.NewGuid() });
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return runs;
        });

        runs.Should().Be(1);
    }

    /// <summary>
    ///     A save with no transaction of its own, where the roll-up interceptor opens one to update the
    ///     parent's aggregate. It opens it in <c>SavingChanges</c> — before EF's own strategy starts — so
    ///     the save itself has to run inside the strategy.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_AnInterceptorOwnedTransaction_IsAllowed()
    {
        using var connection = OpenDatabase();
        var invoiceId = Guid.NewGuid();

        using (var db = CreateContext(connection))
        {
            db.Invoices.Add(new Invoice { Id = invoiceId });
            await new EfCoreUnitOfWork(db).SaveChangesAsync();
        }

        using (var db = CreateContext(connection))
        {
            db.Lines.Add(new Line { Id = Guid.NewGuid(), InvoiceId = invoiceId, Amount = 12m });
            await new EfCoreUnitOfWork(db).SaveChangesAsync();
        }

        using (var db = CreateContext(connection))
            (await db.Invoices.SingleAsync()).Subtotal.Should().Be(12m, "the delta was applied inside the save's transaction");
    }
}
