using Pragmatic.Persistence.Repository;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A declared query's <c>[ComplexFilter]</c> on the documents engine (#488), the Sql engine's cases (<c>ComplexFilterTests</c> there): the
///     filter object composed as Pragmatic's <c>[FilterDto]</c> composes it for EF Core, an unset member filtering nothing, the members of an Or
///     group joined with OR, the group joined with the object's other members with AND. One guest's bookings, by phone: anna@RIVA (5 nights),
///     bruno@RIVA (3), anna@MARE (4), anna@ALPE (2). MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class ComplexFilterTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_filter_object_is_composed_as_Pragmatic_composes_it(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Booking>();
        var guest = Guid.NewGuid(); // the databases are shared by the suite's tests: this guest's bookings only
        var bookings = new[] { ("anna@RIVA", 5), ("bruno@RIVA", 3), ("anna@MARE", 4), ("anna@ALPE", 2) }.Select(b =>
        {
            var booking = new Booking { Phone = b.Item1, Nights = b.Item2 };
            booking.SetGuestId(guest);
            return booking;
        }).ToList();
        await session.InsertManyAsync(bookings);
        IReadRepository<Booking> repository = new Booking.DocReadRepository(session);

        async Task<string[]> Phones(BookingFilter? where)
            => [.. (await repository.RunAsync(new FilterBookingsQuery { GuestId = guest, Where = where })).Select(b => b.Phone ?? "")];

        string[] all = ["anna@ALPE", "anna@MARE", "anna@RIVA", "bruno@RIVA"];
        Assert.Equal(all, await Phones(null));
        Assert.Equal(all, await Phones(new BookingFilter())); // nothing set: no filter
        Assert.Equal(all, await Phones(new BookingFilter { Phone = new BookingPhoneFilter() }));
        Assert.Equal(["anna@MARE", "bruno@RIVA"], await Phones(new BookingFilter { Phone = new BookingPhoneFilter { At = "@MARE", By = "bruno" } })); // OR
        Assert.Equal(["anna@MARE", "anna@RIVA"], await Phones(new BookingFilter { MinNights = 4 }));
        Assert.Equal(["anna@RIVA"], await Phones(new BookingFilter { Phone = new BookingPhoneFilter { At = "@RIVA" }, MinNights = 4 })); // the group AND the member
        Assert.Equal(["anna@MARE"], await Phones(new BookingFilter { Phone = new BookingPhoneFilter { At = "@MARE", By = "bruno" }, MinNights = 4 }));
    }
}
