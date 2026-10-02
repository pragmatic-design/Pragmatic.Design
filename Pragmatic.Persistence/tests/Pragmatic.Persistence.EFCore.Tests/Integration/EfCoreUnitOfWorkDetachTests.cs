using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     <see cref="IUnitOfWork.Detach" />: a save that failed must not leave its entity for the next
///     save to retry.
/// </summary>
/// <remarks>
///     <para>
///         A change tracker keeps what it was given whether the write succeeded or not. Without this,
///         one refused row makes <b>every later commit of the same request</b> fail with the same
///         error — so an import committing row by row cannot report one bad row and carry on, which is
///         the entire promise of <c>[CommitStrategy(CommitMode.PerStep)]</c>.
///     </para>
///     <para>
///         Found by measuring, not by reading: a list of members with one repeated key committed the
///         rows before it and then answered 500, because the action's closing save met the duplicate a
///         second time. SQLite is used here because the failure has to be a real constraint violation.
///     </para>
/// </remarks>
public sealed class EfCoreUnitOfWorkDetachTests
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

    /// <summary>
    ///     Writes one row, then leaves a second with the same key pending after a refused save.
    /// </summary>
    /// <remarks>
    ///     The first is detached before the duplicate is added so the clash happens at the database
    ///     rather than in the tracker: EF refuses two tracked instances of one key outright, which is a
    ///     different failure from the one under test.
    /// </remarks>
    private static async Task<TestProduct> FailedInsertAsync(TestDbContext db, IUnitOfWork uow)
    {
        var id = Guid.NewGuid();

        var first = new TestProduct { PersistenceId = id, Name = "First" };
        db.Add(first);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        uow.Detach(first);

        var duplicate = new TestProduct { PersistenceId = id, Name = "Repeated" };
        db.Add(duplicate);
        await Assert.ThrowsAnyAsync<Exception>(() => uow.SaveChangesAsync()).ConfigureAwait(false);

        return duplicate;
    }

    /// <summary>
    ///     The shape the defect had: save, fail, save something unrelated, fail again for the old reason.
    /// </summary>
    [Fact]
    public async Task WithoutDetaching_AFailedEntityBreaksEveryLaterSave()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var duplicate = await FailedInsertAsync(db, uow);

        // The duplicate is still pending, so a later, unrelated write drags it along.
        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Unrelated" });
        await Assert.ThrowsAnyAsync<Exception>(() => uow.SaveChangesAsync());

        duplicate.Should().NotBeNull();
    }

    /// <summary>
    ///     And with it: the failure costs its own row and nothing else.
    /// </summary>
    [Fact]
    public async Task AfterDetaching_ALaterSaveSucceeds()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var duplicate = await FailedInsertAsync(db, uow);

        uow.Detach(duplicate);

        var unrelated = new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Unrelated" };
        db.Add(unrelated);
        await uow.SaveChangesAsync();

        var written = await db.Products
            .CountAsync(p => p.PersistenceId == unrelated.PersistenceId)
            ;

        written.Should().Be(1, "the row that failed is forgotten, so the one after it is written");
    }

    /// <summary>
    ///     Detaching is forgetting, not deleting: what was already committed stays committed.
    /// </summary>
    [Fact]
    public async Task Detaching_DoesNotRemoveWhatWasAlreadyWritten()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var kept = new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Kept" };
        db.Add(kept);
        await uow.SaveChangesAsync();

        uow.Detach(kept);
        await uow.SaveChangesAsync();

        var survivors = await db.Products
            .CountAsync(p => p.PersistenceId == kept.PersistenceId)
            ;

        survivors.Should().Be(1);
    }
}
