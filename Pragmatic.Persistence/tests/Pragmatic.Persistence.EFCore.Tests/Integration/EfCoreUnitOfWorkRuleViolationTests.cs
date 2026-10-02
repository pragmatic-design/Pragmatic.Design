using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.UnitOfWork;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A rule the database enforced arrives as a domain error, not as a provider exception.
/// </summary>
/// <remarks>
///     <para>
///         A unique index, a foreign key, a null or length constraint are rules the application
///         declared — <c>[LogicKey]</c>, a relation, <c>[Required]</c> — and the database is only where
///         they are enforced. Before this, violating one produced an unhandled <b>500</b> with a stack
///         trace in the response: a declared rule presented as a crash.
///     </para>
///     <para>
///         The classification was already written, with parsers for four providers and a heuristic
///         fallback, in <c>Pragmatic.Result.EFCore</c> — and nothing called it. This is the caller.
///     </para>
/// </remarks>
public sealed class EfCoreUnitOfWorkRuleViolationTests
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

    private static async Task<TestProduct> WriteOneAsync(TestDbContext db, IUnitOfWork uow, Guid id)
    {
        var first = new TestProduct { PersistenceId = id, Name = "First" };
        db.Add(first);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        uow.Detach(first);
        return first;
    }

    [Fact]
    public async Task ADuplicateKey_ArrivesAsARuleViolation()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var id = Guid.NewGuid();
        await WriteOneAsync(db, uow, id);

        db.Add(new TestProduct { PersistenceId = id, Name = "Repeated" });

        var violation = await Assert.ThrowsAsync<PersistenceRuleViolationException>(
            () => uow.SaveChangesAsync());

        violation.Error.Should().NotBeNull(
            "the point is the error it carries, not that something was thrown");
        violation.Error.StatusCode.Should().BeInRange(400, 499,
            "a violated declaration is the caller's problem, not a server fault");
    }

    /// <summary>
    ///     The provider exception is kept underneath: nothing is hidden, only reframed.
    /// </summary>
    /// <remarks>
    ///     This test was named for that and asserted <c>Error.Code</c> instead — which is non-empty on
    ///     every error there is. The unit of work went through a Result-returning classifier that had
    ///     already swallowed the exception, so there was nothing underneath to find and nothing said so.
    /// </remarks>
    [Fact]
    public async Task TheProviderExceptionIsStillReachable()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var id = Guid.NewGuid();
        await WriteOneAsync(db, uow, id);
        db.Add(new TestProduct { PersistenceId = id, Name = "Repeated" });

        var violation = await Assert.ThrowsAsync<PersistenceRuleViolationException>(
            () => uow.SaveChangesAsync());

        violation.InnerException.Should().BeOfType<DbUpdateException>(
            "the classification is a reframing, and what it was classified from must stay reachable");
    }

    /// <summary>
    ///     A concurrency conflict is not reframed: it stays the exception EF Core threw.
    /// </summary>
    /// <remarks>
    ///     Two writers meeting on one row is not a rule the schema enforces, and the generated
    ///     repository answers it with <c>ConcurrencyError</c> — which it can only do while this is still
    ///     a <see cref="DbUpdateConcurrencyException" />. Classified, it would become a
    ///     <c>DbConflictError</c>, the same type a duplicate key produces, and a lost update would be
    ///     indistinguishable from a violated unique index.
    /// </remarks>
    [Fact]
    public async Task AConcurrencyConflict_StaysItsOwnException()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var article = new TestArticle
        {
            PersistenceId = Guid.NewGuid(), Title = "First", Content = "Body"
        };
        db.Add(article);
        await uow.SaveChangesAsync();

        // Deleted underneath us, then updated: EF expects one row affected and finds none.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"Articles\" WHERE \"PersistenceId\" = {0}", article.PersistenceId);

        article.Title = "Renamed";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => uow.SaveChangesAsync());
    }

    /// <summary>
    ///     Deleting a row something still references is a conflict that names both: what is in
    ///     use, and what uses it.
    /// </summary>
    /// <remarks>
    ///     It was a 400 <c>DB_CONSTRAINT</c> with nothing on the wire but a generic title: a delete the
    ///     declared relation refused read like a malformed request, and nobody could say what held the row.
    ///     SQLite's message names no constraint, so this also covers the reading by the model alone: the
    ///     one relation that points at the deleted type.
    /// </remarks>
    [Fact]
    public async Task DeletingARowSomethingReferences_IsAnInUseConflict_NamingBoth()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        var category = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Tools" };
        var product = new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Hammer", CategoryId = category.PersistenceId };
        db.AddRange(category, product);
        await uow.SaveChangesAsync();
        uow.Detach(product);

        db.Remove(category);

        var violation = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => uow.SaveChangesAsync());

        var inUse = violation.Error.Should().BeOfType<Pragmatic.Result.EntityFrameworkCore.DbInUseError>().Subject;
        inUse.StatusCode.Should().Be(409);
        inUse.Code.Should().Be("ENTITY_IN_USE");
        inUse.EntityType.Should().Be(nameof(TestCategory));
        inUse.UsedBy.Should().Be(nameof(TestProduct));
    }

    /// <summary>
    ///     The control: a reference to a row that does not exist is still a constraint error — the request
    ///     named something missing, which is not something else holding a row.
    /// </summary>
    [Fact]
    public async Task AReferenceToAMissingRow_IsStillAConstraintError()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Orphan", CategoryId = Guid.NewGuid() });

        var violation = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => uow.SaveChangesAsync());

        violation.Error.Code.Should().Be("DB_CONSTRAINT");
    }

    /// <summary>
    ///     A save with nothing wrong still returns its row count, unchanged.
    /// </summary>
    [Fact]
    public async Task AnOrdinarySave_IsUntouched()
    {
        using var db = CreateContext();
        IUnitOfWork uow = new EfCoreUnitOfWork(db);

        db.Add(new TestProduct { PersistenceId = Guid.NewGuid(), Name = "Fine" });

        (await uow.SaveChangesAsync()).Should().Be(1);
    }
}
