using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     <see cref="IUnitOfWork.DiscardChanges" />: what a failed attempt tracked is forgotten, so the attempt
///     that runs again writes only its own rows.
/// </summary>
/// <remarks>
///     The mutation invoker calls it before every retry under <c>[ResiliencePolicy]</c>;
///     <c>AMutationUnderAPolicyIsRetriedTests</c> measures that path over a fake of this unit of work.
///     This is the real one.
/// </remarks>
public sealed class EfCoreUnitOfWorkDiscardChangesTests
{
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
    public async Task AfterDiscarding_WhatWasPendingIsNotWritten()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Pending" });
        uow.DiscardChanges();
        await uow.SaveChangesAsync();

        (await db.Products.CountAsync()).Should().Be(0, "the pending row was forgotten, not saved");
    }

    /// <summary>The control: without discarding, the same pending row is written.</summary>
    [Fact]
    public async Task WithoutDiscarding_WhatWasPendingIsWritten()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Pending" });
        await uow.SaveChangesAsync();

        (await db.Products.CountAsync()).Should().Be(1);
    }

    /// <summary>Discarding is forgetting, not deleting: what was committed stays.</summary>
    [Fact]
    public async Task Discarding_DoesNotRemoveWhatWasAlreadyWritten()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Kept" });
        await uow.SaveChangesAsync();

        uow.DiscardChanges();
        await uow.SaveChangesAsync();

        (await db.Products.CountAsync()).Should().Be(1);
    }
}
