using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using PragmaticDocumentSamples.Callers;
using PragmaticDocumentSamples.Entities;
using PragmaticDocumentSamples.Queries;

namespace PragmaticDocumentSamples.Flows;

/// <summary>
///     Pragmatic's entities (<c>[Entity]</c>, <c>[Relation.*]</c>, the traits) on both backends: the filter families per caller and mode, a join
///     whose inner side keeps them, a count across Cosmos DB's partitions, the repository contract with specifications compiled where they are
///     written. The rows: RIVA (acme, desk-north), MARE (acme, desk-south), ALPE (other); anna's stays at RIVA and MARE, bruno's at RIVA, one of
///     anna's deleted; anna's and bruno's published reviews, anna's draft. The caller is anna, of acme, holding desk-north.
/// </summary>
internal static class EntitySamples
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession session)
    {
        var checks = new Checks(backend);
        using var tenant = TenantScope.BeginScope("acme");
        // The schema the generator describes (DefaultDocSchema), applied as a host would at its start, twice: the second run finds it there.
        var schema = await DefaultDocSchema.EnsureAsync(session);
        checks.Check("Pragmatic: the schema created (indexes on MongoDB, containers on Cosmos DB)", !schema.Unchanged);
        checks.Check("Pragmatic: the schema unchanged on its second run", (await DefaultDocSchema.EnsureAsync(session)).Unchanged);

        var reviewIds = await SeedAsync(session);
        var toggle = new QueryFilterToggle();
        var filters = new PragmaticDocFilters(new SampleUser("anna"), new TenantScope(), new SampleScopes("desk-north"), toggle);
        var anna = Caller(session, filters);

        checks.Check("Pragmatic: the caller's properties (tenant, scope)", (await NamesAsync(anna)).SequenceEqual(["RIVA"]));
        checks.Check("Pragmatic: the caller's stays (owner, soft delete)", (await NightsAsync(anna)).SequenceEqual([3, 4]));
        checks.Check("Pragmatic: the reviews the caller may read (the rule)", (await anna.Query<Review>().ToListAsync()).Select(r => r.Text).SequenceEqual(["anna@RIVA"]));
        checks.Check("Pragmatic: the caller's stays counted (across partitions on Cosmos DB)", await anna.Query<Reservation>().CountAsync() == 2);
        var joined = await anna.Query<Reservation>().Join(anna.Query<Property>(), r => r.PropertyId, p => p.PersistenceId, (r, p) => p.Name + ":" + r.Nights).ToListAsync();
        checks.Check("Pragmatic: a join, its inner side filtered too", joined.Order(StringComparer.Ordinal).SequenceEqual(["RIVA:3"]));
        using (toggle.UseMode(FilterMode.Admin))
            checks.Check("Pragmatic: Admin, every owner and scope, the tenant kept", (await NamesAsync(anna)).SequenceEqual(["MARE", "RIVA"]) && (await NightsAsync(anna)).SequenceEqual([2, 3, 4]));
        using (toggle.UseMode(FilterMode.Raw))
            checks.Check("Pragmatic: Raw, every row", (await NamesAsync(anna)).SequenceEqual(["ALPE", "MARE", "RIVA"]) && (await NightsAsync(anna)).SequenceEqual([2, 3, 4, 9]));

        IReadRepository<Review> reviews = new Review.DocReadRepository(anna);
        checks.Check("Pragmatic: the repository by key", (await reviews.GetByIdAsync(reviewIds["anna@RIVA"]))?.Text == "anna@RIVA");
        checks.Check("Pragmatic: the repository by key, a row the caller cannot see", await reviews.GetByIdAsync(reviewIds["bruno@RIVA"]) is null);
        checks.Check("Pragmatic: the repository by a specification (its captured value)", await reviews.CountAsync(ReviewsByText.StartingWith("anna")) == 1);
        using (toggle.UseMode(FilterMode.Admin))
        {
            var either = await reviews.FindAsync(ReviewsByText.StartingWith("anna") | ReviewsByText.StartingWith("bruno"));
            checks.Check("Pragmatic: a composed specification, Admin", either.Select(r => r.Text).Order(StringComparer.Ordinal).SequenceEqual(["anna@RIVA", "bruno@RIVA"]));
            checks.Check("Pragmatic: a negated one", !await reviews.ExistsAsync(!Spec<Review>.True));
        }

        checks.Add(await QuerySamples.RunAsync(backend, anna, toggle));
        checks.Add(await FamilySamples.RunAsync(backend, session, anna, toggle));
        checks.Add(await UnitOfWorkSamples.RunAsync(backend, anna)); // after the reads: it changes the rows
        return checks.Run;
    }

    /// <summary>Another session on the same database, for the caller <paramref name="filters" /> describes.</summary>
    public static DocSession Caller(DocSession session, IDocFilters filters)
        => session.Mongo is { } mongo ? new DocSession(mongo, session.Database) { Filters = filters } : new DocSession(session.Cosmos!, session.Database) { Filters = filters };

    private static async Task<string[]> NamesAsync(DocSession session) => (await session.Query<Property>().ToListAsync()).Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<int[]> NightsAsync(DocSession session) => (await session.Query<Reservation>().ToListAsync()).Select(r => r.Nights).Order().ToArray();

    /// <summary>The rows, written whoever reads (the session has no filters); the reviews' keys by text.</summary>
    private static async Task<Dictionary<string, Guid>> SeedAsync(DocSession session)
    {
        Property Property(string name, string tenantId, string scope)
        {
            var property = new Property { Name = name, TenantId = tenantId };
            property.GrantScope(scope);
            return property;
        }

        var (riva, mare, alpe) = (Property("RIVA", "acme", "desk-north"), Property("MARE", "acme", "desk-south"), Property("ALPE", "other", "desk-north"));
        await session.InsertManyAsync([riva, mare, alpe]);
        var stays = new List<Reservation>();
        foreach (var (owner, at, nights, deleted) in new[] { ("anna", riva, 3, false), ("bruno", riva, 2, false), ("anna", mare, 4, false), ("anna", riva, 9, true) })
        {
            var stay = new Reservation { Nights = nights, IsDeleted = deleted };
            stay.SetOwnerId(owner);
            stay.SetPropertyId(at.Id);
            stays.Add(stay);
        }

        await session.InsertManyAsync(stays);
        var reviews = new List<Review>();
        foreach (var (owner, text, published) in new[] { ("anna", "anna@RIVA", true), ("bruno", "bruno@RIVA", true), ("anna", "anna draft", false) })
        {
            var review = new Review { Text = text, Published = published, TenantId = "acme" };
            review.SetOwnerId(owner);
            reviews.Add(review);
        }

        await session.InsertManyAsync(reviews);
        return reviews.ToDictionary(r => r.Text, r => r.Id);
    }
}
