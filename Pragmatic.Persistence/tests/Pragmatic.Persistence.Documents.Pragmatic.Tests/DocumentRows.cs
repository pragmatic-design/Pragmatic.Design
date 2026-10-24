using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The documents the family tests read, the Sql engine's rows (<c>PragmaticRows</c>) as documents: RIVA (acme, desk-north), MARE (acme,
///     desk-south), ALPE (other, desk-north). Reservations: anna's at RIVA and MARE, bruno's at RIVA, anna's at RIVA deleted. Reviews (acme unless
///     said): anna's published at RIVA, bruno's published, anna's unpublished, anna's published of the tenant other. Written whoever reads.
/// </summary>
internal static class DocumentRows
{
    public static async Task<DocSession> SeededAsync(DocumentServers servers, Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Property>();
            await session.EnsureContainerAsync<Reservation>();
            await session.EnsureContainerAsync<Review>();
        }

        Property Property(string name, string tenant, string scope)
        {
            var property = new Property { Name = name, TenantId = tenant };
            property.GrantScope(scope);
            return property;
        }

        var (riva, mare, alpe) = (Property("RIVA", "acme", "desk-north"), Property("MARE", "acme", "desk-south"), Property("ALPE", "other", "desk-north"));
        await session.InsertManyAsync([riva, mare, alpe]);
        var stays = new List<Reservation>();
        foreach (var (owner, at, nights, deleted) in new[] { ("anna", riva, 3, false), ("bruno", riva, 2, false), ("anna", mare, 4, false), ("anna", riva, 9, true) })
        {
            var stay = new Reservation { CheckIn = DateTimeOffset.UnixEpoch, Nights = nights, IsDeleted = deleted };
            stay.SetOwnerId(owner);
            stay.SetPropertyId(at.Id);
            stays.Add(stay);
        }

        await session.InsertManyAsync(stays);
        var reviews = new List<Review>();
        foreach (var (owner, text, published, tenant) in new[] { ("anna", "anna@RIVA", true, "acme"), ("bruno", "bruno@RIVA", true, "acme"), ("anna", "anna draft", false, "acme"), ("anna", "anna@ALPE", true, "other") })
        {
            var review = new Review { Text = text, Published = published, TenantId = tenant, Stars = text.Length };
            review.SetOwnerId(owner);
            reviews.Add(review);
        }

        await session.InsertManyAsync(reviews);
        return session;
    }

    /// <summary>anna, of acme, holding desk-north, with the toggle her lifts go through.</summary>
    public static PragmaticDocFilters Anna(IQueryFilterToggle toggle, string[]? permissions = null)
        => new(new TestUser("anna", permissions), new Pragmatic.MultiTenancy.TenantScope(), new TestScopes("desk-north"), toggle);
}
