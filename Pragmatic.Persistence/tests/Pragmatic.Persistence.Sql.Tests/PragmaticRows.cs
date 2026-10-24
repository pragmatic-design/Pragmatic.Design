using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;

namespace PragmaticEntities.Tests;

/// <summary>
///     The rows the tests read: RIVA (acme, desk-north), MARE (acme, desk-south), ALPE (other, desk-north). Reservations: anna's at RIVA,
///     MARE and ALPE, bruno's at RIVA. Reviews, published unless said: anna's at RIVA (5 stars) and MARE (4), bruno's at RIVA (3), anna's
///     unpublished at ALPE (2). Room types at RIVA: STD, and SUI deleted. Written in Raw mode, whoever the session's caller is.
/// </summary>
internal static class PragmaticRows
{
    public static async Task SeedAsync(SqlSession session, IQueryFilterToggle toggle)
    {
        using (toggle.UseMode(FilterMode.Raw))
        {
            Property Property(string name, string tenant, string scope)
            {
                var property = new Property { Name = name, TenantId = tenant };
                property.GrantScope(scope);
                return property;
            }

            var (riva, mare, alpe) = (Property("RIVA", "acme", "desk-north"), Property("MARE", "acme", "desk-south"), Property("ALPE", "other", "desk-north"));
            foreach (var property in new[] { riva, mare, alpe })
                await session.InsertAsync(property);
            foreach (var (owner, at) in new[] { ("anna", riva), ("bruno", riva), ("anna", mare), ("anna", alpe) })
            {
                var stay = new Reservation { CheckIn = DateTimeOffset.UnixEpoch };
                stay.SetOwnerId(owner);
                stay.SetPropertyId(at.Id);
                await session.InsertAsync(stay);
            }

            foreach (var (owner, at, published, stars) in new[] { ("anna", riva, true, 5), ("bruno", riva, true, 3), ("anna", mare, true, 4), ("anna", alpe, false, 2) })
            {
                var review = new Review { Text = owner + "@" + at.Name, Published = published, Stars = stars };
                review.SetOwnerId(owner);
                review.SetPropertyId(at.Id);
                await session.InsertAsync(review);
            }

            foreach (var code in new[] { "STD", "SUI" })
            {
                var type = new RoomType { Code = code };
                type.SetPropertyId(riva.Id);
                await session.InsertAsync(type);
                if (code == "SUI")
                    await session.DeleteAsync(type);
            }
        }
    }
}
