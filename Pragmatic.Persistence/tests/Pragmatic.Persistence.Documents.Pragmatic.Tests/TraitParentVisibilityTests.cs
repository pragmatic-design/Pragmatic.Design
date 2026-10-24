using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A trait's child visible only where its parent is, on MongoDB and Cosmos DB, as on the Sql engine (<c>TraitParentVisibilityTests</c> there):
///     the parents' keys the caller may see read first, the child's foreign key among them. The documents: <see cref="DocumentRows" />, a comment
///     on each review and a note on each property; anna owns three reviews (one a draft, one of the tenant other) and holds desk-north.
/// </summary>
[Collection("document servers")]
public class TraitParentVisibilityTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<DocSession> Seeded(Store store)
    {
        var seeded = await DocumentRows.SeededAsync(servers, store);
        if (store == Store.Cosmos)
        {
            await seeded.EnsureContainerAsync<ReviewComment>();
            await seeded.EnsureContainerAsync<PropertyNote>();
        }

        var raw = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));
        List<Review> reviews;
        List<Property> properties;
        using (_toggle.UseMode(FilterMode.Raw))
            (reviews, properties) = (await raw.Query<Review>().ToListAsync(), await raw.Query<Property>().ToListAsync());
        var comments = reviews.Select(r => { var c = new ReviewComment { Text = "on " + r.Text }; c.SetReviewId(r.Id); return c; }).ToList();
        var notes = properties.Select(p => { var n = new PropertyNote { Text = "on " + p.Name }; n.SetPropertyId(p.Id); return n; }).ToList();
        await seeded.InsertManyAsync(comments);
        await seeded.InsertManyAsync(notes);
        return raw;
    }

    private static async Task<List<string>> Texts<T>(Task<List<T>> rows, Func<T, string> text) => (await rows).Select(text).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_childs_documents_are_those_of_the_parents_the_caller_may_see(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        Assert.Equal(["on anna draft", "on anna@ALPE", "on anna@RIVA"], await Texts(session.Query<ReviewComment>().ToListAsync().AsTask(), c => c.Text));
        Assert.Equal(["on ALPE", "on RIVA"], await Texts(session.Query<PropertyNote>().ToListAsync().AsTask(), n => n.Text));
        Assert.Equal(2, await session.Query<PropertyNote>().CountAsync());
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(4, await session.Query<ReviewComment>().CountAsync()); // lifted with the parent's permission filters
    }
}
