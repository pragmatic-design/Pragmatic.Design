using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Privacy;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The privacy adapters [PersonalData] generates for entities the Sql engine persists, on the five databases: a subject's rows found by key
///     from the subject (a guest) through its bookings to their requests, soft-deleted ones included, exported, then erased furthest first, as
///     the orchestrator orders the steps. ann is the subject; bob's rows stay as they are.
/// </summary>
[Collection("pragmatic databases")]
public class PrivacyAdaptersTests(PragmaticDatabases databases)
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

    private async Task SeedAsync(SqlSession session)
    {
        using (_toggle.UseMode(FilterMode.Raw))
        {
            foreach (var (email, name, phones) in new[] { ("ann@example.org", "Ann", new[] { "111", "222" }), ("bob@example.org", "Bob", new[] { "333" }) })
            {
                var guest = new Guest { Email = email, FullName = name };
                await session.InsertAsync(guest);
                foreach (var phone in phones)
                {
                    var booking = new Booking { Phone = phone, Nights = 2 };
                    booking.SetGuestId(guest.Id);
                    await session.InsertAsync(booking);
                    foreach (var deleted in new[] { false, true })
                    {
                        var request = new BookingRequest { Text = $"{name} {phone} {deleted}" };
                        request.SetBookingId(booking.Id);
                        await session.InsertAsync(request);
                        if (deleted)
                            await session.DeleteAsync(request);
                    }
                }
            }
        }
    }

    private async Task<(List<Guest> Guests, List<Booking> Bookings, List<BookingRequest> Requests)> Stored(SqlSession session)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return (await session.Query<Guest>().ToListAsync(), await session.Query<Booking>().ToListAsync(), await session.Query<BookingRequest>().ToListAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_subjects_rows_are_found_by_key_exported_and_erased(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("dpo"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await SeedAsync(session);
        var registry = new Registry();
        using var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("dpo"), new TenantScope());
        var (guests, bookings, requests) = (new Guest.SqlRepository(unitOfWork), new Booking.SqlRepository(unitOfWork), new BookingRequest.SqlRepository(unitOfWork));

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
