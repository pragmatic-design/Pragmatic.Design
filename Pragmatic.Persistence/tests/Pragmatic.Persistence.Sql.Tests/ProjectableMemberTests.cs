using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A declared projection that reads a <c>[Projectable]</c> member of the entity on the five databases (#506): <c>Booking.Score</c> is no
///     column, its body (<c>Nights * 10 + (Phone == null ? 0 : 1)</c>) is computed from the two columns the statement reads.
/// </summary>
[Collection("pragmatic databases")]
public class ProjectableMemberTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_projection_reads_a_projectable_member_as_its_body_computes_it(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        var guest = new Guest { Email = "ada@example.org" };
        await session.InsertAsync(guest);
        foreach (var (nights, phone) in new[] { (3, "+39 3"), (1, "+39 1"), (2, (string?)null) })
        {
            var booking = new Booking { Nights = nights, Phone = phone };
            booking.SetGuestId(guest.Id);
            await session.InsertAsync(booking);
        }

        IReadRepository<Booking> bookings = new Booking.SqlReadRepository(session);
        var scores = await bookings.RunAsync(new BookingScoresQuery { GuestId = guest.Id });

        // Nights × 10, plus one with a phone: the one without is the one without the extra point.
        Assert.Equal([("+39 1", 11), (null, 20), ("+39 3", 31)], scores.Select(s => (s.Phone, s.Score)));
    }
}
