using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A declared query with <c>MapInMemory = true</c> on the documents engine (#505): the page's documents read whole, the navigation its Selector
///     reads filled from the related documents found by their keys, the Selector run over them. A booking whose guest is not there is left out,
///     as the INNER JOIN of a required navigation leaves it. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class InMemoryMappingTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_query_that_maps_in_memory_fills_its_navigation_and_runs_the_selector(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Guest>();
            await session.EnsureContainerAsync<Booking>();
        }

        var prefix = "M-" + Guid.NewGuid().ToString("N")[..10];
        var (ada, bob) = (new Guest { Email = $"ada-{prefix}@example.org" }, new Guest { Email = $"bob-{prefix}@example.org" });
        await session.InsertManyAsync([ada, bob]);
        var bookings = new[] { (1, ada.Id), (2, bob.Id), (3, ada.Id), (4, Guid.NewGuid()) }.Select(b =>
        {
            var booking = new Booking { Phone = $"{prefix}-{b.Item1}", Nights = b.Item1 };
            booking.SetGuestId(b.Item2);
            return booking;
        }).ToList();
        await session.InsertManyAsync(bookings);

        IReadRepository<Booking> repository = new Booking.DocReadRepository(session);
        var letters = await repository.RunAsync(new BookingLettersQuery { Phone = prefix });

        Assert.Equal([$"{ada.Email} x1", $"{bob.Email} x2", $"{ada.Email} x3"], letters.Select(l => l.Line)); // the fourth's guest is not there
    }
}
