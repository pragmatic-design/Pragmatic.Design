using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Privacy;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The privacy adapters [PersonalData] generates for entities the documents engine persists, on MongoDB and Cosmos DB, as on the Sql engine
///     (<c>PrivacyAdaptersTests</c> there): a subject's documents found by key from the guest through its bookings to their requests, the
///     relations kept as keys, soft-deleted ones included, exported, then erased furthest first. ann is the subject; bob's stay as they are.
/// </summary>
[Collection("document servers")]
public class PrivacyAdaptersTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    /// <summary>The registry the application keeps: <c>ref-ann</c> names ann by her email.</summary>
    private sealed class Registry : ISubjectRegistry
    {
        public ValueTask<string> GetOrCreateReferenceAsync(string subjectType, string identifier, CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<string?> FindReferenceAsync(string subjectType, string identifier, CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<string?> ResolveIdentityAsync(string subjectRef, CancellationToken ct = default) => ValueTask.FromResult(subjectRef == "ref-ann" ? "ann@example.org" : null);

        public ValueTask<bool> ForgetAsync(string subjectRef, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<DocSession> SeededAsync(DocumentServers servers, Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Guest>();
            await session.EnsureContainerAsync<Booking>();
            await session.EnsureContainerAsync<BookingRequest>();
        }

        var (guests, bookings, requests) = (new List<Guest>(), new List<Booking>(), new List<BookingRequest>());
        foreach (var (email, name, phones) in new[] { ("ann@example.org", "Ann", new[] { "111", "222" }), ("bob@example.org", "Bob", new[] { "333" }) })
        {
            var guest = new Guest { Email = email, FullName = name };
            guests.Add(guest);
            foreach (var phone in phones)
            {
                var booking = new Booking { Phone = phone, Nights = 2 };
                booking.SetGuestId(guest.Id);
                bookings.Add(booking);
                foreach (var deleted in new[] { false, true })
                {
                    var request = new BookingRequest { Text = $"{name} {phone} {deleted}", IsDeleted = deleted };
                    request.SetBookingId(booking.Id);
                    requests.Add(request);
                }
            }
        }

        await session.InsertManyAsync(guests);
        await session.InsertManyAsync(bookings);
        await session.InsertManyAsync(requests);
        return session;
    }

    private async Task<(List<Guest> Guests, List<Booking> Bookings, List<BookingRequest> Requests)> Stored(DocSession session)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return (await session.Query<Guest>().ToListAsync(), await session.Query<Booking>().ToListAsync(), await session.Query<BookingRequest>().ToListAsync());
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_subjects_documents_are_found_by_key_exported_and_erased(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = DocumentServers.As(await SeededAsync(servers, store), DocumentRows.Anna(_toggle));
        var registry = new Registry();
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("dpo"), new TenantScope());
        var (guests, bookings, requests) = (new Guest.DocRepository(unitOfWork), new Booking.DocRepository(unitOfWork), new BookingRequest.DocRepository(unitOfWork));

        Assert.Single(await new GuestPersonalDataSource(guests, _toggle, registry).CollectAsync("ref-ann"));
        Assert.Equal(2, (await new BookingPersonalDataSource(bookings, guests, _toggle, registry).CollectAsync("ref-ann")).Count);
        Assert.Equal(4, (await new BookingRequestPersonalDataSource(requests, bookings, guests, _toggle, registry).CollectAsync("ref-ann")).Count); // two soft deleted
        Assert.Empty(await new GuestPersonalDataSource(guests, _toggle, registry).CollectAsync("ref-unknown"));

        // Furthest from the subject first, as the orchestrator runs them: the requests, the bookings, the guest.
        Assert.Equal(4, (await new BookingRequestErasureStep(requests, bookings, guests, _toggle, unitOfWork, registry).EraseAsync("ref-ann")).ErasedCount);
        Assert.Equal(2, (await new BookingErasureStep(bookings, guests, _toggle, unitOfWork, registry).EraseAsync("ref-ann")).ErasedCount);
        Assert.Equal(1, (await new GuestErasureStep(guests, _toggle, unitOfWork, registry).EraseAsync("ref-ann")).ErasedCount);

        var stored = await Stored(session);
        Assert.Equal(["Bob 333 False", "Bob 333 True"], stored.Requests.Select(r => r.Text).Order(StringComparer.Ordinal)); // ann's deleted for good
        Assert.Equal([null, null, "333"], stored.Bookings.Select(b => b.Phone).Order(StringComparer.Ordinal));
        var ann = stored.Guests.Single(g => g.Email != "bob@example.org");
        Assert.NotEqual("ann@example.org", ann.Email); // her pseudonym
        Assert.Null(ann.FullName);
    }
}
