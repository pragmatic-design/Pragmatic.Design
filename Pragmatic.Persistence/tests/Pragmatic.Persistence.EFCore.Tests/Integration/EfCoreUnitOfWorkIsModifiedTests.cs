using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     <see cref="IUnitOfWork.IsModified" /> on EF Core: whether the next save writes the entity's values. A generated action invoker asks it
///     of each entity the action loaded, and validates the ones that answer yes (#628).
/// </summary>
public sealed class EfCoreUnitOfWorkIsModifiedTests
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

    /// <summary>A row written, then read again by a fresh context: tracked as unchanged, as an action's load leaves it.</summary>
    private static async Task<(TestDbContext Db, TestProduct Row)> LoadedAsync()
    {
        var db = CreateContext();
        var id = Guid.NewGuid();
        db.Add(new TestProduct { PersistenceId = id, Name = "Read" });
        await db.SaveChangesAsync().ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return (db, await db.Products.SingleAsync(p => p.PersistenceId == id).ConfigureAwait(false));
    }

    [Fact]
    public async Task ARowLoadedAndLeftAlone_IsNotModified()
    {
        var (db, row) = await LoadedAsync();
        using var _ = db;

        new EfCoreUnitOfWork(db).IsModified(row).Should().BeFalse();
    }

    /// <summary>
    ///     A property set by the caller, the tracker not told and its automatic detection off: the unit of work detects the entity's
    ///     changes itself, so the answer does not depend on how the context is configured.
    /// </summary>
    [Fact]
    public async Task ARowWithAPropertySet_IsModified_EvenWithAutomaticDetectionOff()
    {
        var (db, row) = await LoadedAsync();
        using var _ = db;
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        row.Name = "Changed";

        new EfCoreUnitOfWork(db).IsModified(row).Should().BeTrue();
    }

    [Fact]
    public void AnAddedRow_IsModified()
    {
        using var db = CreateContext();
        var row = new TestProduct { PersistenceId = Guid.NewGuid(), Name = "New" };
        db.Add(row);

        new EfCoreUnitOfWork(db).IsModified(row).Should().BeTrue();
    }

    /// <summary>One the context does not track is not one its save writes.</summary>
    [Fact]
    public void AnUntrackedRow_IsNotModified()
    {
        using var db = CreateContext();

        new EfCoreUnitOfWork(db).IsModified(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Elsewhere" }).Should().BeFalse();
    }

    /// <summary>One marked for deletion has no values to write, and nothing to validate.</summary>
    [Fact]
    public async Task ARowMarkedForDeletion_IsNotModified()
    {
        var (db, row) = await LoadedAsync();
        using var _ = db;
        db.Remove(row);

        new EfCoreUnitOfWork(db).IsModified(row).Should().BeFalse();
    }
}
