using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Temporal.Clock;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The tag actions and list query [HasTags] generates for tags the documents engine persists, on MongoDB and Cosmos DB, as on the Sql engine
///     (<c>TagActionsTests</c> there): the link and the shared tag through their repositories, the tag's usage count kept on its row version, the
///     per-entity limit (three) counted before the write. anna, in acme, holds desk-north and the property's view-all: RIVA and MARE are hers to tag.
/// </summary>
[Collection("document servers")]
public class TagActionsTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static readonly string PropertyViewAll = new PropertyNote.ParentVisibilityFilter(new TestScopes("desk-north"), new TestUser("x")).BypassPermission;

    private sealed class Caller(DocSession session, IQueryFilterToggle toggle, TestUser user)
    {
        public DocUnitOfWork UnitOfWork { get; } = new(session, new FixedClock(UnitOfWorkTests.Now), user, new TenantScope());

        public TestUser User { get; } = user;

        public IServiceProvider Services { get; } = new ServiceCollection().AddSingleton(toggle).BuildServiceProvider();

        public IClock Clock { get; } = new TestInstant();

        public Task<Result<Guid, IError>> Add(Guid property, string value)
            => new AddPropertyTagAction.Invoker(new PropertyTag.DocRepository(UnitOfWork), new PropertyTagLink.DocRepository(UnitOfWork), UnitOfWork, User, Clock, Services, Services)
                .InvokeAsync(new AddPropertyTagAction { PropertyId = property, TagValue = value });

        public Task<VoidResult<IError>> Remove(Guid property, Guid tag)
            => new RemovePropertyTagAction.Invoker(new PropertyTag.DocRepository(UnitOfWork), new PropertyTagLink.DocRepository(UnitOfWork), UnitOfWork, Services, Services)
                .InvokeAsync(new RemovePropertyTagAction { PropertyId = property, TagId = tag });
    }

    /// <summary>The clock the actions stamp with: UnitOfWorkTests' instant.</summary>
    private sealed class TestInstant : IClock
    {
        public DateTimeOffset UtcNow => UnitOfWorkTests.Now;
        public DateTimeOffset Now => UnitOfWorkTests.Now;
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);
        public DateOnly Today => UtcToday;
        public TimeOnly UtcTimeOfDay => TimeOnly.FromDateTime(UtcNow.UtcDateTime);
        public TimeOnly TimeOfDay => UtcTimeOfDay;
        public TimeProvider GetTimeProvider() => new FixedClock(UnitOfWorkTests.Now);
    }

    private static TestUser Anna => new("anna", [PropertyViewAll]);

    private async Task<(DocSession Session, Guid Riva, Guid Mare)> Seeded(Store store)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
        {
            await seeded.EnsureContainerAsync<PropertyTag>();
            await seeded.EnsureContainerAsync<PropertyTagLink>();
        }

        var session = DocumentServers.As(seeded, new PragmaticDocFilters(Anna, new TenantScope(), new TestScopes("desk-north"), _toggle));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = await session.Query<Property>().ToListAsync();
            return (session, properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "MARE").Id);
        }
    }

    private static async Task<List<(string Value, string Display)>> Tags(DocSession session, Guid property)
    {
        var services = new ServiceCollection().AddScoped<IReadRepository<PropertyTagLink>>(_ => new PropertyTagLink.DocReadRepository(session)).BuildServiceProvider();
        var page = await new ListPropertyTagsQuery.Invoker(services).RunAsync(new ListPropertyTagsQuery { PropertyId = property });
        return page.Value!.Items.Select(t => (t.Value, t.DisplayValue)).OrderBy(t => t.Value, StringComparer.Ordinal).ToList();
    }

    private async Task<int?> UsageCount(DocSession session, string value)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return (await session.Query<PropertyTag>().ToListAsync()).SingleOrDefault(t => t.Value == value)?.UsageCount;
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_tag_is_added_once_listed_limited_and_removed_with_its_orphan(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, _) = await Seeded(store);
        var caller = new Caller(session, _toggle, Anna);

        var blue = (await caller.Add(riva, " Blue ")).Value;
        Assert.Equal(blue, (await caller.Add(riva, "blue")).Value); // normalized, idempotent
        Assert.Equal(1, await UsageCount(session, "blue"));

        var red = (await caller.Add(riva, "Red")).Value;
        await caller.Add(riva, "Green");
        Assert.IsType<ForbiddenError>((await caller.Add(riva, "Yellow")).Error); // three at most
        Assert.Equal([("blue", "Blue"), ("green", "Green"), ("red", "Red")], await Tags(session, riva));

        Assert.True((await caller.Remove(riva, red)).IsSuccess);
        Assert.Equal([("blue", "Blue"), ("green", "Green")], await Tags(session, riva));
        Assert.Null(await UsageCount(session, "red")); // nobody links it any more: removed
        Assert.IsType<NotFoundError>((await caller.Remove(riva, red)).Error);
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_shared_tags_count_follows_its_links(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, mare) = await Seeded(store);
        var caller = new Caller(session, _toggle, Anna);

        var blue = (await caller.Add(riva, "blue")).Value;
        Assert.Equal(blue, (await caller.Add(mare, "blue")).Value);
        Assert.Equal(2, await UsageCount(session, "blue"));

        Assert.True((await caller.Remove(riva, blue)).IsSuccess);
        Assert.Equal(1, await UsageCount(session, "blue")); // still linked to MARE: kept
        Assert.Equal([("blue", "blue")], await Tags(session, mare));
    }

    /// <summary>The row version the actions' retry stands on: two units of work raise one tag's count, the second save is refused, not lost.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Two_raises_of_one_tags_count_do_not_lose_one(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva, _) = await Seeded(store);
        var blue = (await new Caller(session, _toggle, Anna).Add(riva, "blue")).Value;

        var first = new Caller(session, _toggle, Anna);
        var second = new Caller(session, _toggle, Anna);
        var mine = (await new PropertyTag.DocRepository(first.UnitOfWork).GetByIdAsync(blue))!;
        var theirs = (await new PropertyTag.DocRepository(second.UnitOfWork).GetByIdAsync(blue))!;
        mine.UsageCount++;
        theirs.UsageCount++;
        await first.UnitOfWork.SaveChangesAsync();
        var refused = await Assert.ThrowsAsync<PersistenceRuleViolationException>(() => second.UnitOfWork.SaveChangesAsync());
        Assert.IsType<ConcurrencyError>(refused.Error);
        Assert.Equal(2, await UsageCount(session, "blue"));
    }
}
