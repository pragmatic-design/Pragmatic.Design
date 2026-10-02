using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Deep tests for [HasAccessScopes] and [HasOwner]+[HasAccessScopes] visibility.
///     Extends DataOwnershipTests with scope-matching, bypass, and combined OR logic scenarios.
///     Entities tested:
///       - Guest: [HasOwner]+[HasAccessScopes] → DataAccessFilter (OR logic, bypass: booking.guest.view-all)
///       - Invoice: [HasAccessScopes] → ScopedDataFilter (bypass: billing.invoice.view-all)
///       - Reservation: [HasOwner] → OwnershipFilter (bypass: booking.reservation.view-all)
///     Scope model: DefaultUserScopeResolver produces "user:{id}", "role:{role}", "scope:{claim}".
/// </summary>
public class ScopedVisibilityTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // ScopedEntity on Invoice: user with matching scope sees data
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_UserWithMatchingScope_SeesData()
    {
        // Create a reservation + confirm it to generate an invoice (as default test-user)
        var reservationId = await CreateAndConfirmReservationAsync();

        // Get the invoice
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // The default test-user has "billing.invoice.view-all" permission (in DefaultPermissions) so
        // they can see it, and CreateClientAs layers those same defaults — so this client holds the
        // bypass. ⚠️ Which means this case exercises the bypass, not a scope match; the scope match is
        // ScopedEntity_SearchEndpoint_ReturnsOnlyScopedItems, which withholds view-all on purpose.
        // The invoice's AccessScopes are populated with user:{creatorId} by ScopeInterceptor, at
        // SaveChanges — not by any invoker.
        using var scopedClient = CreateClientAs("test-user", "Scoped User");

        var response = await scopedClient.GetAsync($"/api/invoices/{invoiceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "User whose scopes match the entity's AccessScopes should see the entity");

        var invoiceBody = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        invoiceBody.GetProperty("id").GetGuid().Should().Be(invoiceId,
            "Returned invoice should be the one we requested");
        invoiceBody.GetProperty("reservationId").GetGuid().Should().Be(reservationId,
            "Invoice should reference the correct reservation");
    }

    // =========================================================================
    // ScopedEntity on Invoice: user without matching scope does NOT see data
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_UserWithoutScope_DoesNotSeeData()
    {
        // Create an invoice as default test-user
        var reservationId = await CreateAndConfirmReservationAsync();

        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // Create a different user WITHOUT view-all permission → ScopedDataFilter applies.
        // This user has no matching scope ("user:no-scope-user" won't match "user:test-user").
        using var noScopeClient = CreateClientWithPermissions(
            "billing.invoice.read",
            "billing.invoice.create");

        var response = await noScopeClient.GetAsync($"/api/invoices/{invoiceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ScopedDataFilter should hide invoices from users whose scopes don't overlap");
    }

    // =========================================================================
    // ScopedEntity: view-all permission bypasses the filter
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_ViewAllPermission_BypassesFilter()
    {
        // Create an invoice as default test-user
        var reservationId = await CreateAndConfirmReservationAsync();

        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // User with billing.invoice.view-all bypasses ScopedDataFilter
        using var adminClient = CreateClientWithPermissions(
            "billing.invoice.read",
            "billing.invoice.view-all");

        var response = await adminClient.GetAsync($"/api/invoices/{invoiceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "User with billing.invoice.view-all should bypass ScopedDataFilter and see all invoices");

        var invoiceBody = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        invoiceBody.GetProperty("id").GetGuid().Should().Be(invoiceId,
            "Admin with view-all should receive the exact invoice requested");
        invoiceBody.GetProperty("invoiceNumber").GetString().Should().NotBeNullOrEmpty(
            "Invoice should have an auto-generated invoice number");
    }

    // =========================================================================
    // Combined [HasOwner]+[HasAccessScopes] on Guest: owner sees data via OR logic
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_CombinedOwnershipAndScoped_ORLogic()
    {
        // Guest has both [HasOwner] and [HasAccessScopes] → DataAccessFilter uses OR logic:
        // entity.OwnerId == userId || entity.AccessScopes.Any(s => userScopes.Contains(s))
        // This means the owner sees the guest even without matching scopes.

        using var ownerClient = CreateClientAs("combined-owner", "Combined Owner");

        // Create a guest as combined-owner
        var guestResponse = await ownerClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Scoped",
            lastName = "Visibility",
            email = $"scoped.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestResponse.EnsureSuccessStatusCode();
        var guestData = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guestData.GetProperty("id").GetGuid();

        // Verify the owner sees the guest (OwnerId matches — OR logic first branch)
        var ownerResponse = await ownerClient.GetAsync($"/api/guests/{guestId}");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "DataAccessFilter OR logic: owner should see their own guest even without scope match");

        var guestBody = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        guestBody.GetProperty("id").GetGuid().Should().Be(guestId);
        guestBody.GetProperty("firstName").GetString().Should().Be("Scoped",
            "Returned guest should have the correct firstName");

        // A different user without matching scopes and without view-all should NOT see it
        using var otherClient = CreateClientWithPermissions(
            "booking.guest.read",
            "booking.guest.create");

        var otherResponse = await otherClient.GetAsync($"/api/guests/{guestId}");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "DataAccessFilter should block access when user is not owner AND scopes don't overlap");
    }

    // =========================================================================
    // ScopedEntity: search returns only visible items (scoped filtering on list)
    // =========================================================================

    [Fact]
    public async Task ScopedEntity_SearchEndpoint_ReturnsOnlyScopedItems()
    {
        // Create invoices as two different users
        using var userAClient = CreateClientAs("scope-user-a", "Scope User A");
        var resIdA = await CreateReservationAndConfirmAs(userAClient);

        using var userBClient = CreateClientAs("scope-user-b", "Scope User B");
        var resIdB = await CreateReservationAndConfirmAs(userBClient);

        // User A searches invoices — should only see their own (scoped visibility)
        // Note: user A does NOT have billing.invoice.view-all
        using var userASearchClient = CreateClientAs("scope-user-a", "Scope User A");
        userASearchClient.DefaultRequestHeaders.Remove("X-User-Permissions");
        userASearchClient.DefaultRequestHeaders.Add("X-User-Permissions",
            "booking.reservation.create,booking.reservation.read,booking.reservation.update," +
            "booking.guest.read,booking.guest.create," +
            "catalog.property.read,catalog.property.create," +
            "catalog.room-type.read,catalog.room-type.create," +
            "billing.invoice.read,billing.invoice.create");

        var searchResponse = await userASearchClient.GetAsync("/api/invoices/search");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var invoiceItems = searchResult.GetProperty("items");

        // I confirmed the reservation; the invoice it raised is mine — without view-all, and without
        // anybody granting me a scope by hand.
        //
        // ⚠️ This asserted the opposite for a while, and both versions were wrong about the cause.
        // First it asserted this and passed for the wrong reason: the client is the SAME user id as the
        // one that created the reservation with a deliberately narrower permission list, and the
        // permission cache was keyed by user id alone, so the first client's wildcard grant answered
        // here too — it measured scoped visibility while running as view-all. Then it was
        // turned into a characterisation saying the invoice carried the dispatcher's scope. It carried
        // NO scope: nothing in the framework wrote AccessScopes at all, so the generated filter was
        // false for every invoice and every caller. ScopeInterceptor is the write side.
        invoiceItems.EnumerateArray()
            .Any(inv => inv.TryGetProperty("reservationId", out var rId) && rId.GetGuid() == resIdA)
            .Should().BeTrue(
                "the row was created under my request, so it carries my scope and I can see it "
                + "without the view-all bypass");

        // The control, and it is the one that matters: a different user does not see it. Without this,
        // "user A sees the invoice" is satisfied by a scope filter that stopped filtering.
        using var userBSearchClient = CreateClientAs("scope-user-b", "Scope User B");
        userBSearchClient.DefaultRequestHeaders.Remove("X-User-Permissions");
        userBSearchClient.DefaultRequestHeaders.Add("X-User-Permissions",
            "billing.invoice.read,billing.invoice.create");

        var userBResult = await (await userBSearchClient.GetAsync("/api/invoices/search"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var userBItems = userBResult.GetProperty("items");

        userBItems.EnumerateArray()
            .Any(inv => inv.TryGetProperty("reservationId", out var rId) && rId.GetGuid() == resIdA)
            .Should().BeFalse("user B did not cause user A's invoice and holds no scope that reaches it");

        userBItems.EnumerateArray()
            .Any(inv => inv.TryGetProperty("reservationId", out var rId) && rId.GetGuid() == resIdB)
            .Should().BeTrue("and user B does see the invoice raised by their own reservation");
    }

    // =========================================================================
    // OwnedEntity + view-all on Guest: admin sees all guests
    // =========================================================================

    [Fact]
    public async Task DataAccessFilter_AdminWithViewAll_SeesAllGuests()
    {
        // Create guests as different owners
        using var ownerAClient = CreateClientAs("vis-owner-a", "Vis Owner A");
        var guestAResponse = await ownerAClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "VisA",
            lastName = "Test",
            email = $"visa.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestAResponse.EnsureSuccessStatusCode();
        var guestAId = (await guestAResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        using var ownerBClient = CreateClientAs("vis-owner-b", "Vis Owner B");
        var guestBResponse = await ownerBClient.PostAsJsonAsync("/api/guests", new
        {
            firstName = "VisB",
            lastName = "Test",
            email = $"visb.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestBResponse.EnsureSuccessStatusCode();
        var guestBId = (await guestBResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        // Admin with booking.guest.view-all sees both
        using var adminClient = CreateClientWithPermissions(
            "booking.guest.read",
            "booking.guest.view-all");

        var responseA = await adminClient.GetAsync($"/api/guests/{guestAId}");
        var responseB = await adminClient.GetAsync($"/api/guests/{guestBId}");

        responseA.StatusCode.Should().Be(HttpStatusCode.OK,
            "Admin with view-all should see guest owned by user A");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK,
            "Admin with view-all should see guest owned by user B");

        var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        bodyA.GetProperty("id").GetGuid().Should().Be(guestAId);
        bodyA.GetProperty("firstName").GetString().Should().Be("VisA",
            "Admin should see correct guest A data via view-all bypass");

        var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        bodyB.GetProperty("id").GetGuid().Should().Be(guestBId);
        bodyB.GetProperty("firstName").GetString().Should().Be("VisB",
            "Admin should see correct guest B data via view-all bypass");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Guid> CreateAndConfirmReservationAsync()
    {
        var reservationId = await CreateFullReservationAsync();
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return reservationId;
    }

    private async Task<Guid> CreateFullReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Scope",
            lastName = "Test",
            email = $"scope.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"SV-{Guid.NewGuid():N}"[..12],
            name = $"ScopeProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Scope Room",
            code = $"SV{Guid.NewGuid():N}"[..3],
            baseRate = 120m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(14).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(17).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<Guid> CreateReservationAndConfirmAs(HttpClient userClient)
    {
        // Use main Client for entity setup (has all permissions) but reservation as specific user
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "ScopeUser",
            lastName = "Test",
            email = $"su.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"SC-{Guid.NewGuid():N}"[..12],
            name = $"ScTest-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "SC Room",
            code = $"SC{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        // Create reservation as the specific user
        var response = await userClient.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(20).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(23).ToString("O"),
                numberOfGuests = 1
            }
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Confirm as the same user
        var confirmResponse = await PostWithClientAsync(userClient,
            $"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return reservationId;
    }
}
