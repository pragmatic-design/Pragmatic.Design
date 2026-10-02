using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Tests the transaction-state tracking, savepoints, and untyped Add added to
///     <see cref="EfCoreUnitOfWork"/> this session. Uses SQLite (a relational provider) because the
///     EF InMemory provider does not support transactions or savepoints.
/// </summary>
public sealed class EfCoreUnitOfWorkSavepointTests
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

    [Fact]
    public async Task State_TransitionsThroughCommitLifecycle()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        uow.State.Should().Be(TransactionState.None);
        uow.IsCommitted.Should().BeFalse();

        var tx = await uow.BeginTransactionAsync();
        uow.State.Should().Be(TransactionState.Active);

        await tx.CommitAsync();
        uow.State.Should().Be(TransactionState.Committed);
        uow.IsCommitted.Should().BeTrue();
    }

    [Fact]
    public async Task State_Rollback_SetsRolledBack()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var tx = await uow.BeginTransactionAsync();
        await tx.RollbackAsync();

        uow.State.Should().Be(TransactionState.RolledBack);
        uow.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task SavepointAsync_WithoutTransaction_Throws()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var act = async () => await uow.SavepointAsync("sp").ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RollbackToSavepointAsync_WithoutTransaction_Throws()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var act = async () => await uow.RollbackToSavepointAsync("sp").ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Savepoint_RollbackToSavepoint_DiscardsOnlyWorkAfterSavepoint()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var tx = await uow.BeginTransactionAsync();

        db.Customers.Add(new TestCustomer { FullName = "before-savepoint", Email = "before@test.dev" });
        await uow.SaveChangesAsync();

        await uow.SavepointAsync("sp");

        db.Customers.Add(new TestCustomer { FullName = "after-savepoint", Email = "after@test.dev" });
        await uow.SaveChangesAsync();

        await uow.RollbackToSavepointAsync("sp");
        await tx.CommitAsync();

        var survivors = db.Customers.AsNoTracking().ToList();
        survivors.Should().ContainSingle();
        survivors[0].FullName.Should().Be("before-savepoint");
    }

    [Fact]
    public async Task Add_UntypedEntity_PersistsAfterSaveChanges()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        uow.Add(new TestCustomer { FullName = "untyped", Email = "untyped@test.dev" });
        await uow.SaveChangesAsync();

        db.Customers.AsNoTracking().Should().ContainSingle(c => c.FullName == "untyped");
    }
}
