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
        var comments = reviews.Select(r => new ReviewComment { Content = "on " + r.Text, ReviewId = r.Id }).ToList();
        var notes = properties.Select(p => new PropertyNote { Content = "on " + p.Name, AuthorId = "anna", AuthorName = "Anna", PropertyId = p.Id }).ToList();
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
        // anna's reviews of acme: anna@ALPE is hers and the tenant other's, and so is its comment (EF's "ParentTenant").
        Assert.Equal(["on anna draft", "on anna@RIVA"], await Texts(session.Query<ReviewComment>().ToListAsync().AsTask(), c => c.Content));
        // The properties sharing desk-north and of acme: RIVA (ALPE shares the scope and is the tenant other's).
        Assert.Equal(["on RIVA"], await Texts(session.Query<PropertyNote>().ToListAsync().AsTask(), n => n.Content));
        Assert.Equal(1, await session.Query<PropertyNote>().CountAsync());
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(3, await session.Query<ReviewComment>().CountAsync()); // lifted with the parent's permission filters, not across the tenant
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_deleted_child_is_out_of_the_reads(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        Property riva;
        using (_toggle.UseMode(FilterMode.Raw))
            riva = (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA");
        await session.InsertManyAsync([new PropertyNote { Content = "gone", AuthorId = "anna", AuthorName = "Anna", PropertyId = riva.Id, IsDeleted = true }]);

        Assert.Equal(["on RIVA"], await Texts(session.Query<PropertyNote>().ToListAsync().AsTask(), n => n.Content));
        // Both halves of the soft delete lifted, as on EF: the filter's type and the name "SoftDelete".
        using (_toggle.Disable(typeof(PropertyNote.SoftDeleteFilter)))
        using (_toggle.DisableQueryFilter("SoftDelete"))
            Assert.Equal(["gone", "on RIVA"], await Texts(session.Query<PropertyNote>().ToListAsync().AsTask(), n => n.Content));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_childs_tenant_is_its_parents_until_lifted_by_name(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        // Lifted by name, as EF's IgnoreQueryFilters(["ParentTenant"]) with the entity's filters disabled: every note.
        using (_toggle.DisableQueryFilter("ParentTenant"))
        using (_toggle.Disable(typeof(PropertyNote)))
            Assert.Equal(3, await session.Query<PropertyNote>().CountAsync());
    }
}
