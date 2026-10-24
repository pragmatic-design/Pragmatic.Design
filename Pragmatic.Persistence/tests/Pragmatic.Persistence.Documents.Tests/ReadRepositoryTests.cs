using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's <c>IReadRepository&lt;T&gt;</c> on the documents engine (the generated <c>Review.DocReadRepository</c>) and its specifications
///     compiled where they are written (<c>Spec&lt;T&gt;.Where</c>, their captured values read off the lambda), composed with <c>&amp;</c>,
///     <c>|</c>, <c>!</c>, on MongoDB and Cosmos DB. The oracle is the same specification read in memory, within what the caller may read.
/// </summary>
[Collection("document servers")]
public class ReadRepositoryTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_repository_answers_the_specifications_within_the_callers_filters(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        using var admin = _toggle.UseMode(FilterMode.Admin); // every owner's, the tenant and the rule kept
        var session = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));
        IReadRepository<Review> reviews = new Review.DocReadRepository(session);
        var visible = await reviews.FindAsync(Spec<Review>.True); // what the caller may read: acme's, published
        Assert.Equal(["anna@RIVA", "bruno@RIVA"], visible.Select(r => r.Text).Order(StringComparer.Ordinal));

        var specifications = new (string Name, Specification<Review> Spec)[]
        {
            ("prefix", ReviewSpecifications.StartingWith("anna")),
            ("stars", ReviewSpecifications.AtLeast(10)),
            ("and", ReviewSpecifications.StartingWith("anna") & ReviewSpecifications.AtLeast(5)),
            ("or", ReviewSpecifications.StartingWith("bruno") | ReviewSpecifications.AtLeast(10)),
            ("not", !ReviewSpecifications.StartingWith("anna")),
            ("drafts", ReviewSpecifications.Drafts), // none visible: the rule holds
            ("list", ReviewSpecifications.AnyOf(["bruno@RIVA", "nobody"])),
            ("twice", ReviewSpecifications.AtLeast(5) & !ReviewSpecifications.AtLeast(10)), // one leaf twice, its values its own each time
        };
        foreach (var (name, specification) in specifications)
        {
            var matches = specification.ToExpression().Compile();
            var expected = visible.Where(matches).Select(r => r.Text).Order(StringComparer.Ordinal).ToList();
            Assert.True(expected.SequenceEqual((await reviews.FindAsync(specification)).Select(r => r.Text).Order(StringComparer.Ordinal)), name);
            Assert.True(expected.Count == await reviews.CountAsync(specification), name + ": count");
            Assert.True((expected.Count > 0) == await reviews.ExistsAsync(specification), name + ": exists");
        }

        var first = visible.Single(r => r.Text == "anna@RIVA");
        Assert.Equal("anna@RIVA", (await reviews.GetByIdAsync(first.Id))?.Text);
        Assert.Equal("anna@RIVA", (await reviews.FirstOrDefaultAsync(ReviewSpecifications.StartingWith("anna@R")))?.Text);
        Assert.Throws<NotSupportedException>(() => reviews.Query());
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_row_the_caller_may_not_read_is_not_found_by_key(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        List<Reservation> all;
        using (_toggle.UseMode(FilterMode.Raw))
            all = await DocumentServers.As(seeded, DocumentRows.Anna(_toggle)).Query<Reservation>().ToListAsync();
        IReadRepository<Reservation> reservations = new Reservation.DocReadRepository(DocumentServers.As(seeded, DocumentRows.Anna(_toggle)));

        Assert.NotNull(await reservations.GetByIdAsync(all.Single(r => r.Nights == 3).Id)); // hers
        Assert.Null(await reservations.GetByIdAsync(all.Single(r => r.Nights == 2).Id)); // bruno's
        Assert.Null(await reservations.GetByIdAsync(all.Single(r => r.Nights == 9).Id)); // deleted
        Assert.Equal(2, await reservations.CountAsync(Spec<Reservation>.True)); // across partitions on Cosmos DB: counted range by range
    }
}
