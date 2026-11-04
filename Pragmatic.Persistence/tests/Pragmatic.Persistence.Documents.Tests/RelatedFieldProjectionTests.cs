using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A declared query's <c>[MapProperty("Nav.Field")]</c> through a reference navigation on the documents engine (#486): a document keeps the
///     relation as its key, so the related documents are read by the keys of the page's documents and the field read from the one each finds. A
///     booking whose guest is not there reads null, as EF Core's left join gives it. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class RelatedFieldProjectionTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_navigations_field_is_read_from_the_related_document_and_a_missing_one_reads_null(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Guest>();
            await session.EnsureContainerAsync<Booking>();
        }

        var prefix = "P-" + Guid.NewGuid().ToString("N")[..10];
        var ada = new Guest { Email = $"ada-{prefix}@example.org", FullName = "Ada" };
        var bob = new Guest { Email = $"bob-{prefix}@example.org" }; // no name: the related field itself is null
        await session.InsertManyAsync([ada, bob]);

        // Two of Ada's (one key read once), one of Bob's, one whose guest is not there.
        var bookings = new[] { (1, ada.Id), (2, ada.Id), (3, bob.Id), (4, Guid.NewGuid()) }.Select(b =>
        {
            var booking = new Booking { Phone = $"{prefix}-{b.Item1}", Nights = b.Item1 };
            booking.SetGuestId(b.Item2);
            return booking;
        }).ToList();
        await session.InsertManyAsync(bookings);

        IReadRepository<Booking> repository = new Booking.DocReadRepository(session);
        var cards = await repository.RunAsync(new BookingCardsQuery { Phone = prefix });

        Assert.Equal(
        [
            (1, ada.Email, "Ada"),
            (2, ada.Email, "Ada"),
            (3, bob.Email, null),
            (4, null, null),
        ], cards.Select(c => (c.Nights, c.GuestEmail, c.GuestName)));

        // Into a property that is not nullable: the related field, and null for a missing related document, as EF Core's left join reads it.
        Assert.Equal([ada.Email, ada.Email, bob.Email, null], cards.Select(c => (string?)c.GuestContact));
    }
}
