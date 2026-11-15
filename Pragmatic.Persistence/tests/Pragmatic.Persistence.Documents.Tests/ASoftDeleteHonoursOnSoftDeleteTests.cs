using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result.Database;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A relation's <c>OnSoftDelete</c> on the documents engine, on MongoDB and Cosmos DB (#753): what EF Core's <c>SoftDeleteInterceptor</c> and
///     the Sql engine's save do, the unit of work's save does. A shelf (<see cref="OsdShelf" />) is soft-deleted through its repository, and each
///     of the documents that point at it declares one rule.
/// </summary>
/// <remarks>
///     Without the rules a soft delete is an update no store checks a reference against, so the shelf went whatever pointed at it, and the docs
///     said the documents engine did not apply them.
/// </remarks>
[Collection("document servers")]
public class ASoftDeleteHonoursOnSoftDeleteTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Restrict_refuses_the_soft_delete_while_a_live_referrer_exists_and_writes_nothing(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        await WriteAsync(() => session.InsertManyAsync([Referrer(new OsdBook { Title = "Kept" }, b => b.SetShelfId(shelf))]));

        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => SoftDeleteAsync(session, shelf));

        var inUse = Assert.IsType<DbInUseError>(refused.Error);
        Assert.Equal((nameof(OsdShelf), nameof(OsdBook)), (inUse.EntityType, inUse.UsedBy));
        Assert.False((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted, "a refused soft delete writes nothing");
    }

    /// <summary>The control: a referrer already soft-deleted is not live, and does not hold the shelf.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Restrict_lets_the_soft_delete_through_when_every_referrer_is_deleted(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        await WriteAsync(() => session.InsertManyAsync([Referrer(new OsdBook { Title = "Gone", IsDeleted = true, DeletedAt = UnitOfWorkTests.Now }, b => b.SetShelfId(shelf))]));

        await SoftDeleteAsync(session, shelf);

        Assert.True((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Without_OnSoftDelete_the_referrer_keeps_its_key(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        var poster = Referrer(new OsdPoster { Caption = "Kept" }, p => p.SetShelfId(shelf));
        await WriteAsync(() => session.InsertManyAsync([poster]));

        await SoftDeleteAsync(session, shelf);

        Assert.True((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted);
        Assert.Equal(shelf, (await RawAsync(session, s => s.Query<OsdPoster>().ToListAsync())).Single(p => p.Id == poster.Id).ShelfId);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task SetNull_empties_the_referrers_key_in_the_same_save(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        var label = Referrer(new OsdLabel { Text = "Fiction" }, l => l.SetShelfId(shelf));
        await WriteAsync(() => session.InsertManyAsync([label]));

        await SoftDeleteAsync(session, shelf);

        Assert.Null((await RawAsync(session, s => s.Query<OsdLabel>().ToListAsync())).Single(l => l.Id == label.Id).ShelfId);
    }

    /// <summary>The binder goes with the shelf, flagged with the shelf's instant, and its sheet goes with it: its own rule in turn.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Cascade_flags_the_referrer_with_the_principals_instant_and_one_level_further(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        var binder = Referrer(new OsdBinder { Title = "Minutes" }, b => b.SetShelfId(shelf));
        await WriteAsync(() => session.InsertManyAsync([binder]));
        var sheet = Referrer(new OsdSheet { Text = "Page 1" }, s => s.SetBinderId(binder.Id));
        await WriteAsync(() => session.InsertManyAsync([sheet]));

        await SoftDeleteAsync(session, shelf);

        var flaggedBinder = (await RawAsync(session, s => s.Query<OsdBinder>().ToListAsync())).Single(b => b.Id == binder.Id);
        var flaggedSheet = (await RawAsync(session, s => s.Query<OsdSheet>().ToListAsync())).Single(s => s.Id == sheet.Id);
        Assert.Equal((true, UnitOfWorkTests.Now), (flaggedBinder.IsDeleted, flaggedBinder.DeletedAt!.Value));
        Assert.Equal((true, UnitOfWorkTests.Now), (flaggedSheet.IsDeleted, flaggedSheet.DeletedAt!.Value));
    }

    /// <summary>
    ///     A cascade refused one level down (the binder it reaches is held by a clip) writes nothing: the rules run before the first write, so the
    ///     shelf and the binder stay live on Cosmos DB too, which has no transaction to undo a write with.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_cascade_refused_further_down_writes_nothing(Store store)
    {
        var session = await OpenAsync(store);
        var shelf = await ShelfAsync(session);
        var binder = Referrer(new OsdBinder { Title = "Minutes" }, b => b.SetShelfId(shelf));
        await WriteAsync(() => session.InsertManyAsync([binder]));
        await WriteAsync(() => session.InsertManyAsync([Referrer(new OsdClip { Colour = "red" }, c => c.SetBinderId(binder.Id))]));

        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => SoftDeleteAsync(session, shelf));

        var inUse = Assert.IsType<DbInUseError>(refused.Error);
        Assert.Equal((nameof(OsdBinder), nameof(OsdClip)), (inUse.EntityType, inUse.UsedBy));
        Assert.False((await RawAsync(session, s => s.Query<OsdShelf>().ToListAsync())).Single(s => s.Id == shelf).IsDeleted);
        Assert.False((await RawAsync(session, s => s.Query<OsdBinder>().ToListAsync())).Single(b => b.Id == binder.Id).IsDeleted);
    }

    /// <summary>A database of its own for anna, every container the entities above need created on Cosmos DB.</summary>
    private async Task<DocSession> OpenAsync(Store store)
    {
        var session = servers.Session(store, DocumentRows.Anna(_toggle));
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<OsdShelf>();
            await session.EnsureContainerAsync<OsdBook>();
            await session.EnsureContainerAsync<OsdPoster>();
            await session.EnsureContainerAsync<OsdLabel>();
            await session.EnsureContainerAsync<OsdBinder>();
            await session.EnsureContainerAsync<OsdSheet>();
            await session.EnsureContainerAsync<OsdClip>();
        }

        return session;
    }

    private async Task<Guid> ShelfAsync(DocSession session)
    {
        var shelf = new OsdShelf { Name = "s" + Guid.NewGuid().ToString("N")[..8] };
        await WriteAsync(() => session.InsertManyAsync([shelf]));
        return shelf.Id;
    }

    /// <summary>The shelf read through its repository and removed, as a generated delete mutation does, then saved.</summary>
    private static async Task SoftDeleteAsync(DocSession session, Guid shelf)
    {
        var unitOfWork = UnitOfWorkTests.UnitOfWork(session);
        var shelves = new OsdShelf.DocRepository(unitOfWork);
        shelves.Remove((await shelves.GetByIdAsync(shelf))!);
        await unitOfWork.SaveChangesAsync();
    }

    private static T Referrer<T>(T document, Action<T> pointAt)
    {
        pointAt(document);
        return document;
    }

    /// <summary>A seed written as it is, outside the unit of work.</summary>
    private async Task WriteAsync(Func<Task> write)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            await write();
    }

    /// <summary>Every document, the soft-deleted ones among them: what the store holds.</summary>
    private async Task<TResult> RawAsync<TResult>(DocSession session, Func<DocSession, ValueTask<TResult>> read)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return await read(session);
    }
}
