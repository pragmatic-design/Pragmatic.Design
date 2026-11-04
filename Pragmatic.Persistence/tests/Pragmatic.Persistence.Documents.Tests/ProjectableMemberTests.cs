using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A declared projection that reads a <c>[Projectable]</c> member of the entity on MongoDB and Cosmos DB (#506): <c>Booking.Score</c> is no
///     field of the document, its body (<c>Nights * 10 + (Phone == null ? 0 : 1)</c>) runs in the projection over the two fields it names.
/// </summary>
[Collection("document servers")]
public class ProjectableMemberTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_projection_reads_a_projectable_member_as_its_body_computes_it(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Guest>();
            await session.EnsureContainerAsync<Booking>();
        }

        var guest = new Guest { Email = $"ada-{Guid.NewGuid():N}@example.org" };
        await session.InsertManyAsync([guest]);
        var bookings = new[] { (3, "+39 3"), (1, "+39 1"), (2, (string?)null) }.Select(b =>
        {
            var booking = new Booking { Nights = b.Item1, Phone = b.Item2 };
            booking.SetGuestId(guest.Id);
            return booking;
        }).ToList();
        await session.InsertManyAsync(bookings);

        IReadRepository<Booking> repository = new Booking.DocReadRepository(session);
        var scores = await repository.RunAsync(new BookingScoresQuery { GuestId = guest.Id });

        // Nights × 10, plus one with a phone: the one without is the one without the extra point.
        Assert.Equal([("+39 1", 11), (null, 20), ("+39 3", 31)], scores.Select(s => (s.Phone, s.Score)));
    }
}
