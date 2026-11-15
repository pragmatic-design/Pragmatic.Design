using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result.Database;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A relation's <c>OnSoftDelete</c> on the Sql engine, on the five databases (#752): what EF Core's <c>SoftDeleteInterceptor</c> does, the
///     unit of work's save does. A shelf (<see cref="OsdShelf" />) is soft-deleted through its repository, and each of the rows that point at it
///     declares one rule.
/// </summary>
/// <remarks>
///     Without the rules a soft delete is an update the database's foreign key never sees, so the shelf went whatever pointed at it, and the
///     docs said the Sql engine did not apply them.
/// </remarks>
[Collection("pragmatic databases")]
public class ASoftDeleteHonoursOnSoftDeleteTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Restrict_refuses_the_soft_delete_while_a_live_referrer_exists_and_writes_nothing(SqlDialect dialect)
    {
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        var shelf = await ShelfAsync(session);
        await WriteAsync(() => session.InsertAsync(Referrer(new OsdBook { Title = "Kept" }, b => b.SetShelfId(shelf))));

        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => SoftDeleteAsync(unitOfWork, shelf));

        var inUse = Assert.IsType<DbInUseError>(refused.Error);
        Assert.Equal((nameof(OsdShelf), nameof(OsdBook)), (inUse.EntityType, inUse.UsedBy));
        Assert.False((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted, "a refused soft delete writes nothing");
    }

    /// <summary>The control: a referrer already soft-deleted is not live, and does not hold the shelf.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Restrict_lets_the_soft_delete_through_when_every_referrer_is_deleted(SqlDialect dialect)
    {
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        var shelf = await ShelfAsync(session);
        await WriteAsync(() => session.InsertAsync(Referrer(new OsdBook { Title = "Gone", IsDeleted = true, DeletedAt = UnitOfWorkTests.Now }, b => b.SetShelfId(shelf))));

        await SoftDeleteAsync(unitOfWork, shelf);

        Assert.True((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Without_OnSoftDelete_the_referrer_keeps_its_key(SqlDialect dialect)
    {
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        var shelf = await ShelfAsync(session);
        var poster = Referrer(new OsdPoster { Caption = "Kept" }, p => p.SetShelfId(shelf));
        await WriteAsync(() => session.InsertAsync(poster));

        await SoftDeleteAsync(unitOfWork, shelf);

        Assert.True((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted);
        Assert.Equal(shelf, (await RawAsync(session, s => s.Query<OsdPoster>().ToListAsync())).Single(p => p.Id == poster.Id).ShelfId);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task SetNull_empties_the_referrers_key_in_the_same_save(SqlDialect dialect)
    {
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        var shelf = await ShelfAsync(session);
        var label = Referrer(new OsdLabel { Text = "Fiction" }, l => l.SetShelfId(shelf));
        await WriteAsync(() => session.InsertAsync(label));

        await SoftDeleteAsync(unitOfWork, shelf);

        Assert.Null((await RawAsync(session, s => s.Query<OsdLabel>().ToListAsync())).Single(l => l.Id == label.Id).ShelfId);
    }

    /// <summary>The binder goes with the shelf, flagged with the shelf's instant, and its sheet goes with it: its own rule in turn.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Cascade_flags_the_referrer_with_the_principals_instant_and_one_level_further(SqlDialect dialect)
    {
        var (session, unitOfWork) = await OpenAsync(dialect);
        using var _ = session;
        var shelf = await ShelfAsync(session);
        var binder = Referrer(new OsdBinder { Title = "Minutes" }, b => b.SetShelfId(shelf));
        await WriteAsync(() => session.InsertAsync(binder));
        var sheet = Referrer(new OsdSheet { Text = "Page 1" }, s => s.SetBinderId(binder.Id));
        await WriteAsync(() => session.InsertAsync(sheet));

        await SoftDeleteAsync(unitOfWork, shelf);

        var flaggedBinder = (await RawAsync(session, s => s.Query<OsdBinder>().ToListAsync())).Single(b => b.Id == binder.Id);
        var flaggedSheet = (await RawAsync(session, s => s.Query<OsdSheet>().ToListAsync())).Single(s => s.Id == sheet.Id);
        Assert.Equal((true, UnitOfWorkTests.Now), (flaggedBinder.IsDeleted, flaggedBinder.DeletedAt!.Value));
        Assert.Equal((true, UnitOfWorkTests.Now), (flaggedSheet.IsDeleted, flaggedSheet.DeletedAt!.Value));
    }

    private async Task<(SqlSession Session, SqlUnitOfWork UnitOfWork)> OpenAsync(SqlDialect dialect)
    {
        var anna = new TestUser("anna");
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(anna, new TenantScope(), new TestScopes(), _toggle));
        return (session, new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), anna, new TenantScope()));
    }

    private async Task<Guid> ShelfAsync(SqlSession session)
    {
        var shelf = new OsdShelf { Name = "s" + Guid.NewGuid().ToString("N")[..8] };
        await WriteAsync(() => session.InsertAsync(shelf));
        return shelf.Id;
    }

    /// <summary>The shelf read through its repository and removed, as a generated delete mutation does, then saved.</summary>
    private static async Task SoftDeleteAsync(SqlUnitOfWork unitOfWork, Guid shelf)
    {
        var shelves = new OsdShelf.SqlRepository(unitOfWork);
        shelves.Remove((await shelves.GetByIdAsync(shelf))!);
        await unitOfWork.SaveChangesAsync();
    }

    private static T Referrer<T>(T row, Action<T> pointAt)
    {
        pointAt(row);
        return row;
    }

    /// <summary>A seed written as it is, outside the unit of work, the write typed at each call where the engine compiles it.</summary>
    private async Task WriteAsync(Func<Task> write)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            await write();
    }

    /// <summary>
    ///     Every row, the soft-deleted ones among them: what the database holds. The read is written with its type at each call, where the
    ///     engine compiles it.
    /// </summary>
    private async Task<TResult> RawAsync<TResult>(SqlSession session, Func<SqlSession, ValueTask<TResult>> read)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return await read(session);
    }
}
