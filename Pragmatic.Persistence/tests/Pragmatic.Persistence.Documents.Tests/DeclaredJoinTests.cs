using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A declared query's <c>[Join&lt;T&gt;]</c> by key on the documents engine (#508): the joined documents read by the keys of the matched ones, the
///     result property named with the join's alias read from the one each key finds. A booking whose guest is not there is kept with a null email
///     on a left join, and dropped on an inner one. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class DeclaredJoinTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_declared_join_reads_the_aliased_side_keeps_on_left_and_drops_on_inner(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Guest>();
            await session.EnsureContainerAsync<Booking>();
        }

        var prefix = "J-" + Guid.NewGuid().ToString("N")[..10];
        var ada = new Guest { Email = $"ada-{prefix}@example.org", FullName = "Ada" };
        var bob = new Guest { Email = $"bob-{prefix}@example.org" };
        await session.InsertManyAsync([ada, bob]);

        // Ada's, Bob's, and one whose guest is not there.
        var bookings = new[] { (1, ada.Id), (2, bob.Id), (3, Guid.NewGuid()) }.Select(b =>
        {
            var booking = new Booking { Phone = $"{prefix}-{b.Item1}", Nights = b.Item1 };
            booking.SetGuestId(b.Item2);
            return booking;
        }).ToList();
        await session.InsertManyAsync(bookings);

        IReadRepository<Booking> repository = new Booking.DocReadRepository(session);
        var left = await repository.RunAsync(new BookingContactsQuery { Phone = prefix });
        var inner = await repository.RunAsync(new BookingContactsInnerQuery { Phone = prefix });

        Assert.Equal([(1, ada.Email), (2, bob.Email), (3, null)], left.Select(c => (c.Nights, c.WhoEmail)));
        Assert.Equal([(1, ada.Email), (2, bob.Email)], inner.Select(c => (c.Nights, c.WhoEmail)));
    }
}
