using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     What the unit of work and the query executor log, as a provider receives it: id, name, level,
///     message, the state's pairs and the exception. The values are those Microsoft's generator produced
///     for these methods, so moving them to Pragmatic's call sites (<c>PragmaticLogCallSites</c>) changes
///     none of them for whoever filters, alerts or queries on them.
/// </summary>
public sealed class TheUnitOfWorkAndTheExecutorKeepTheirLogEventsTests : IDisposable
{
    private readonly RecordingLoggerProvider _recorder = new();
    private readonly ServiceProvider _services;

    public TheUnitOfWorkAndTheExecutorKeepTheirLogEventsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_recorder));
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ASaveThatSucceedsThenOneThatFails_LogAsMicrosoftsGeneratorDid()
    {
        using var db = SqliteContext();
        var uow = new EfCoreUnitOfWork(db, _services.GetRequiredService<ILogger<EfCoreUnitOfWork>>());

        var first = new TestAnnouncement { PersistenceId = Guid.NewGuid(), Headline = "Doors open" };
        db.Add(first);
        await uow.SaveChangesAsync();
        uow.Detach(first);

        // Same key, so the clash happens at the database rather than in the change tracker.
        db.Add(new TestAnnouncement { PersistenceId = first.PersistenceId, Headline = "Again" });
        await Assert.ThrowsAnyAsync<Exception>(() => uow.SaveChangesAsync());

        var entries = _recorder.Entries.ToArray();

        var starting = entries.First(e => e.EventId.Name == "LogSaveChangesStarting");
        starting.EventId.Id.Should().Be(1775444903);
        starting.Level.Should().Be(LogLevel.Debug);
        starting.Message.Should().Be("SaveChangesAsync starting");
        starting.Pairs.Select(p => p.Key).Should().Equal("{OriginalFormat}");

        var completed = entries.Single(e => e.EventId.Name == "LogSaveChangesCompleted");
        completed.EventId.Id.Should().Be(1839882990);
        completed.Level.Should().Be(LogLevel.Debug);
        completed.Message.Should().Be("SaveChangesAsync completed: 1 row(s) affected");
        completed.Pairs.Select(p => p.Key).Should().Equal("rowCount", "{OriginalFormat}");
        completed.Pairs[0].Value.Should().BeOfType<int>();
        completed.Pairs[^1].Value.Should().Be("SaveChangesAsync completed: {rowCount} row(s) affected");

        var failed = entries.Single(e => e.EventId.Name == "LogSaveChangesFailed");
        failed.EventId.Id.Should().Be(1920818252);
        failed.Level.Should().Be(LogLevel.Error);
        failed.Message.Should().Be("SaveChangesAsync failed");
        failed.Exception.Should().BeOfType<PersistenceRuleViolationException>();
    }

    [Fact]
    public async Task ATransaction_LogsAsMicrosoftsGeneratorDid()
    {
        using var db = SqliteContext();
        var uow = new EfCoreUnitOfWork(db, _services.GetRequiredService<ILogger<EfCoreUnitOfWork>>());

        using var transaction = await uow.BeginTransactionAsync();

        var entries = _recorder.Entries.ToArray();

        var starting = entries.Single(e => e.EventId.Name == "LogBeginTransactionStarting");
        starting.EventId.Id.Should().Be(1243564714);
        starting.Level.Should().Be(LogLevel.Debug);
        starting.Message.Should().Be("BeginTransactionAsync starting");

        var started = entries.Single(e => e.EventId.Name == "LogTransactionStarted");
        started.EventId.Id.Should().Be(724080884);
        started.Level.Should().Be(LogLevel.Debug);
        started.Message.Should().Be($"Transaction {db.Database.CurrentTransaction!.TransactionId} started");
        started.Pairs.Select(p => p.Key).Should().Equal("transactionId", "{OriginalFormat}");
        started.Pairs[0].Value.Should().BeOfType<Guid>();
    }

    [Fact]
    public async Task AQueryThatSucceedsThenOneThatFails_LogAsMicrosoftsGeneratorDid()
    {
        using var db = TestDbContextFactory.Create();
        db.Products.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Widget", Price = 10m });
        await db.SaveChangesAsync();
        var executor = new EfCoreQueryExecutor(null, null, _services.GetRequiredService<ILogger<EfCoreQueryExecutor>>());

        (await executor.ExecuteAsync(new ProductsPage { Page = 1, PageSize = 20 }, db.Products)).IsSuccess.Should().BeTrue();
        (await executor.ExecuteAsync(new FailingProductsPage { Page = 1, PageSize = 20 }, db.Products)).IsFailure.Should().BeTrue();

        var entries = _recorder.Entries.ToArray();

        var executing = entries.First(e => e.EventId.Name == "LogQueryExecuting");
        executing.EventId.Id.Should().Be(791687133);
        executing.Level.Should().Be(LogLevel.Debug);
        executing.Message.Should().Be("Query 'ProductsPage' executing (cached=False)");
        executing.Pairs.Select(p => p.Key).Should().Equal("queryType", "cached", "{OriginalFormat}");
        executing.Pairs[1].Value.Should().BeOfType<bool>();

        var completed = entries.Single(e => e.EventId.Name == "LogQueryCompleted");
        completed.EventId.Id.Should().Be(294513216);
        completed.Level.Should().Be(LogLevel.Debug);
        completed.Message.Should().Be("Query 'ProductsPage' completed: 1 total record(s), page 1/20");
        completed.Pairs.Select(p => p.Key).Should().Equal("queryType", "totalCount", "page", "pageSize", "{OriginalFormat}");

        var failed = entries.Single(e => e.EventId.Name == "LogQueryFailed");
        failed.EventId.Id.Should().Be(115744526);
        failed.Level.Should().Be(LogLevel.Error);
        failed.Message.Should().Be("Query 'FailingProductsPage' failed with Database");
        failed.Pairs.Select(p => p.Key).Should().Equal("queryType", "errorType", "{OriginalFormat}");
        failed.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // SQLite :memory: lives as long as the open connection the DbContext holds. Relational, so that a
    // duplicate key is refused by the database and a transaction is a real one.
    private static TestDbContext SqliteContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite("DataSource=:memory:").Options;
        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class ProductsPage : IPagedQuery<TestProduct>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => query;
    }

    private sealed class FailingProductsPage : IPagedQuery<TestProduct>
    {
        public int Page { get; init; }
        public int PageSize { get; init; }
        public IQueryable<TestProduct> Apply(IQueryable<TestProduct> query) => throw new InvalidOperationException("No such column");
    }
}
