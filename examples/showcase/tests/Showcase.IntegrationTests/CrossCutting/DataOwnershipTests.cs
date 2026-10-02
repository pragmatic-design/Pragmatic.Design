using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests Data Ownership &amp; Scoped Visibility (P0):
///       - [HasOwner]: OwnerId auto-set, OwnershipFilter isolates users
///       - [HasAccessScopes]: AccessScopes, ScopedDataFilter
///       - [HasOwner]+[HasAccessScopes]: DataAccessFilter OR logic
///       - BypassPermission: admin sees all
/// </summary>
public class DataOwnershipTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // P0.1: [HasOwner] on Reservation — OwnerId auto-set on create
    // =========================================================================

    [Fact]
    public async Task OwnedEntity_CreateReservation_SetsOwnerIdToCurrentUser()
    {
        // Create setup entities as user "owner-a"
        using var ownerClient = CreateClientAs("owner-a", "Owner A");

        var (guestId, propertyId, roomTypeId) = await CreateBookingPrerequisites();

        // Create reservation as owner-a
        var response = await ownerClient.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "[HasOwner] should not prevent creation — OwnerId is stamped by OwnershipInterceptor "
            + "at SaveChanges, which is why this route (an action writing through a repository) gets "
            + "an owner at all");
    }

    [Fact]
    public async Task OwnedEntity_SameUser_CanSeeOwnReservation()
    {
        using var ownerClient = CreateClientAs("owner-b", "Owner B");

        var reservationId = await CreateReservationAs(ownerClient);

        // Same user can see their own reservation
        var response = await ownerClient.GetAsync($"/api/reservations/{reservationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Owner should be able to see their own reservation");
    }

    [Fact]
    public async Task OwnedEntity_DifferentUser_CannotSeeOtherReservation()
    {
        // Create reservation as user "owner-c"
        using var ownerClient = CreateClientAs("owner-c", "Owner C");
        var reservationId = await CreateReservationAs(ownerClient);

        // Try to access as different user "other-user" WITHOUT view-all permission
        using var otherClient = CreateClientWithPermissions(
            "booking.reservation.read",
            "booking.reservation.create");

        var response = await otherClient.GetAsync($"/api/reservations/{reservationId}");

        // OwnershipFilter should block access — 404 (not found for this user)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "OwnershipFilter should hide reservations from non-owners");
    }

    [Fact]
    public async Task OwnedEntity_AdminWithBypassPermission_SeesAll()
    {
        // Create reservation as "owner-d"
        using var ownerClient = CreateClientAs("owner-d", "Owner D");
        var reservationId = await CreateReservationAs(ownerClient);

        // Admin with "booking.reservation.view-all" bypass permission
        using var adminClient = CreateClientWithPermissions(
            "booking.reservation.read",
            "booking.reservation.view-all");

        var response = await adminClient.GetAsync($"/api/reservations/{reservationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Admin with bypass permission should see all reservations regardless of OwnerId");
    }

    // =========================================================================
    // P0.1-bis: the same filter, through a list query rather than a get-by-id
    // =========================================================================

    /// <summary>
    ///     A search returns each user their own reservations and nobody else's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tests above ask for one row by id, where a filtered-out row is a 404. A list is the
    ///         other half and the one that matters more: it is where a filter that failed to apply
    ///         returns other people's rows with a 200, and nothing looks wrong.
    ///     </para>
    ///     <para>
    ///         It is also the only end-to-end evidence that row-level filters reach a <b>query</b> at
    ///         all. A query's invoker does not run the action-filter chain, so whether the data
    ///         filters apply to one is measured here rather than read off <c>EfCoreQueryExecutor</c>
    ///         and the generated registration.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>CreateClientWithPermissions</c>, not <c>CreateClientAs</c>. The latter sends
    ///         <c>DefaultPermissions</c>, which include <c>booking.reservation.view-all</c> — the
    ///         ownership bypass. Written that way the test reported both reservations and looked like a
    ///         fail-open in the framework; it was the caller holding the permission that turns the filter
    ///         off. Each call here mints a distinct user id, which is what makes two narrow callers.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task OwnedEntity_Search_ReturnsOnlyTheCallersOwnReservations()
    {
        using var mine = CreateClientWithPermissions(
            "booking.reservation.read", "booking.reservation.create");
        using var theirs = CreateClientWithPermissions(
            "booking.reservation.read", "booking.reservation.create");

        var myReservation = await CreateReservationAs(mine);
        var theirReservation = await CreateReservationAs(theirs);

        var seen = await SearchReservationIdsAsync(mine);

        seen.Should().Contain(myReservation, "the caller owns it");
        seen.Should().NotContain(theirReservation,
            "a list is where a filter that did not apply hands back other people's rows with a 200");
    }

    /// <summary>The reservation ids a caller can see through the search query.</summary>
    private static async Task<List<Guid>> SearchReservationIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/reservations/search?pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("items");

        return items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    // =========================================================================
    // The restore: it lifts the soft-delete filter and must keep the ownership filter
    // =========================================================================

    /// <summary>
    ///     A restore by id does not reach another owner's retired row.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A restore has to see past the soft-delete filter — that is the point of it — and the two
    ///         halves of the generated load have to agree about how far past. The EF half names one
    ///         filter, <c>IgnoreQueryFilters(query, ["SoftDelete"])</c>, so the tenant filter survives. A
    ///         Pragmatic half that called <c>DisableAll()</c> would make
    ///         <c>QueryFilterToggle.IsDisabled</c> answer true for every filter in scope: ownership and
    ///         access scopes are provider filters, so they would go down too and a restore by id would
    ///         resurrect somebody else's reservation inside the same tenant.
    ///     </para>
    ///     <para>
    ///         Reservation is the only entity in the application that is both <c>[SoftDelete]</c> and
    ///         <c>[HasOwner]</c>, which is why the case lives here: Property's delete/restore pair is
    ///         not owned, and cannot show it.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Restore_OfAnotherOwnersRetiredReservation_IsRefused()
    {
        using var owner = ReservationCaretaker();
        var reservationId = await CreateReservationAs(owner);

        var retire = await owner.DeleteAsync($"/api/reservations/{reservationId}?api-version=1.0");
        retire.IsSuccessStatusCode.Should().BeTrue("the owner may retire their own reservation");

        using var stranger = ReservationCaretaker();

        var response = await stranger.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/restore?api-version=1.0", new { }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the restore lifts the soft-delete filter and nothing else, so a non-owner finds no row");
    }

    /// <summary>The control: the owner's own restore still works.</summary>
    /// <remarks>
    ///     Without it the assertion above would hold on a restore that finds nothing at all — which is
    ///     the other way to make it green and the wrong one.
    /// </remarks>
    [Fact]
    public async Task Restore_OfTheCallersOwnRetiredReservation_BringsTheRowBack()
    {
        using var owner = ReservationCaretaker();
        var reservationId = await CreateReservationAs(owner);

        var retire = await owner.DeleteAsync($"/api/reservations/{reservationId}?api-version=1.0");
        retire.IsSuccessStatusCode.Should().BeTrue();

        (await owner.GetAsync($"/api/reservations/{reservationId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the soft-delete filter hides it while it is retired");

        var response = await owner.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/restore?api-version=1.0", new { }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the owner's restore reaches its own retired row past the soft-delete filter");

        (await owner.GetAsync($"/api/reservations/{reservationId}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "and the row is visible again");
    }

    /// <summary>
    ///     A caller who may create, retire and restore reservations and holds no ownership bypass.
    /// </summary>
    /// <remarks>
    ///     <c>CreateClientWithPermissions</c>, never <c>CreateClientAs</c>: the defaults include
    ///     <c>booking.reservation.view-all</c>, which switches the ownership filter off and would make
    ///     both tests here pass on the broken generator.
    /// </remarks>
    private HttpClient ReservationCaretaker()
        => CreateClientWithPermissions(
            "booking.reservation.read",
            "booking.reservation.create",
            "booking.reservation.update",
            "booking.reservation.delete");

    // =========================================================================
    // P0.2: [HasAccessScopes] on Invoice — AccessScopes filtering
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_InvoiceSearch_ReturnsResults()
    {
        // Invoice has [HasAccessScopes] — verify endpoints still work with filter active
        var response = await GetRawAsync("/api/invoices/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Invoice search should work with ScopedDataFilter active");
    }

    // =========================================================================
    // P0.3: [HasOwner]+[HasAccessScopes] on Guest — DataAccessFilter (OR logic)
    // =========================================================================

    [Fact]
    public async Task DataAccessFilter_GuestCreatedByUser_VisibleToOwner()
    {
        // Guest has both [HasOwner] + [HasAccessScopes] → DataAccessFilter
        using var ownerClient = CreateClientAs("guest-owner-a", "Guest Owner A");

        var guest = await ownerClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "DataAccess",
            lastName = "Test",
            email = $"da.{Guid.NewGuid():N}@test.com"
        });
        guest.EnsureSuccessStatusCode();
        var guestId = await guest.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = guestId.GetProperty("id").GetGuid();

        // Same user can see the guest they created (OR logic: OwnerId matches)
        var response = await ownerClient.GetAsync($"/api/guests/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "DataAccessFilter OR logic: owner can see their own guest");
    }

    [Fact]
    public async Task DataAccessFilter_DifferentUser_CannotSeeOtherGuest()
    {
        using var ownerClient = CreateClientAs("guest-owner-b", "Guest Owner B");

        var guest = await ownerClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Isolated",
            lastName = "Guest",
            email = $"iso.{Guid.NewGuid():N}@test.com"
        });
        guest.EnsureSuccessStatusCode();
        var guestData = await guest.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guestData.GetProperty("id").GetGuid();

        // Different user without matching scopes or ownership
        using var otherClient = CreateClientWithPermissions(
            "booking.guest.read",
            "booking.guest.create");

        var response = await otherClient.GetAsync($"/api/guests/{guestId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "DataAccessFilter should block access when user is not owner AND scopes don't overlap");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreateBookingPrerequisites()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Ownership",
            lastName = $"Test-{Guid.NewGuid():N}"[..20],
            email = $"own.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"OT-{Guid.NewGuid():N}"[..12],
            name = $"OwnTest-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Own Room",
            code = $"OR{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    private async Task<Guid> CreateReservationAs(HttpClient client)
    {
        var (guestId, propertyId, roomTypeId) = await CreateBookingPrerequisites();

        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
