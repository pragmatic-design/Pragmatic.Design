using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     Tests permission enforcement end-to-end across the Showcase app.
///     Covers:
///       - Endpoint-level [RequirePermission] → PragmaticPermissionRequirement
///       - DomainAction-level [RequirePermission] → PermissionAuthorizationFilter
///       - [RequirePolicy] → PolicyEvaluationFilter
///       - Role expansion via RoleExpansionProvider + InMemoryRolePermissionStore
///       - Group expansion via GroupExpansionProvider + InMemoryGroupRoleStore
///       - Anonymous access → 401 Unauthorized
///       - ABAC via IResourceAuthorizer → ResourceAuthorizationFilter
/// </summary>
public class PermissionEnforcementTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // A. Endpoint-level permission enforcement (GetPropertyEndpoint)
    //    [RequirePermission("catalog.property.read")]
    // =========================================================================

    [Fact]
    public async Task GetProperty_WithExactPermission_Returns200()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithPermissions("catalog.property.read");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "User with exact 'catalog.property.read' permission should access GetPropertyEndpoint");
    }

    [Fact]
    public async Task GetProperty_WithoutPermission_Returns403()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithPermissions(); // authenticated, no permissions
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Authenticated user without 'catalog.property.read' should get 403");
    }

    [Fact]
    public async Task GetProperty_Anonymous_Returns401()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateAnonymousClient();
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Anonymous user (no identity headers) should get 401");
    }

    [Fact]
    public async Task GetProperty_WithWrongPermission_Returns403()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithPermissions("booking.reservation.read");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "User with wrong permission ('booking.reservation.read' vs 'catalog.property.read') should get 403");
    }

    /// <summary>
    ///     A refusal has to say which permission was missing.
    /// </summary>
    /// <remarks>
    ///     ASP.NET answers a denied authorization with an empty body, and two independent consumers
    ///     wrote the same result handler to fix it in their own application. The framework carries one
    ///     now — but the first attempt registered it with <c>TryAdd</c> after <c>AddAuthorization()</c>
    ///     had already claimed the slot, so the class shipped and changed nothing. Only reading the
    ///     body tells a handler that works from one that never runs.
    /// </remarks>
    [Fact]
    public async Task GetProperty_WithoutPermission_SaysWhichPermissionIsMissing()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithPermissions();
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("catalog.property.read",
            "a caller cannot fix a refusal that does not name what it needs");
    }

    // =========================================================================
    // A2. [LoadEntity] + [RequirePermission] — authorization runs BEFORE the load
    //     SetGuestPreferencesAction: PUT /api/guests/{guestId}/preferences
    // =========================================================================

    [Fact]
    public async Task SetGuestPreferences_WithoutPermission_MissingGuest_Returns403Not404()
    {
        // The action has [LoadEntity<Guest>] (which 404s for a missing guest) + a
        // [RequirePermission]. The load runs AFTER authorization, so an unauthorized caller is denied
        // 403 even for a non-existent guest — authorization is never bypassed and existence is never
        // leaked via a 404-vs-403 difference.
        using var client = CreateClientWithPermissions(); // authenticated, no permissions
        var missingGuestId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/guests/{missingGuestId}/preferences",
            new { PreferredRoomType = "Suite" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "authorization must run before the [LoadEntity] guard — no 404 existence probing for unauthorized callers");
    }

    [Fact]
    public async Task SetGuestPreferences_WithPermission_MissingGuest_Returns404()
    {
        // Complement: an authorized caller reaches the [LoadEntity] guard, which 404s for a missing guest.
        using var client = CreateClientWithPermissions("booking.guest-preferences.update");
        var missingGuestId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/guests/{missingGuestId}/preferences",
            new { PreferredRoomType = "Suite" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an authorized caller reaches the [LoadEntity] guard, which 404s for a non-existent guest");
    }

    // =========================================================================
    // B. Role expansion — booking-manager includes CatalogReader
    //    CatalogReader has catalog.property.read (exact match)
    // =========================================================================

    [Fact]
    public async Task GetProperty_WithRoleBookingManager_Returns200()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithRoles("booking-manager");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Role 'booking-manager' includes CatalogReader which grants 'catalog.property.read'");
    }

    [Fact]
    public async Task GetProperty_WithRoleCatalogViewer_Returns200()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithRoles("catalog-viewer");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Role 'catalog-viewer' includes CatalogReader which grants 'catalog.property.read'");
    }

    [Fact]
    public async Task GetProperty_WithUnknownRole_Returns403()
    {
        var propertyId = await CreatePropertyAsync();

        using var client = CreateClientWithRoles("nonexistent-role");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Unknown role should not expand to any permissions");
    }

    // =========================================================================
    // C. Group expansion — customer-care → booking-manager + catalog-viewer
    // =========================================================================

    [Fact]
    public async Task GetProperty_WithGroupCustomerCare_Returns200()
    {
        var propertyId = await CreatePropertyAsync();

        // customer-care group → booking-manager + catalog-viewer roles → CatalogReader → catalog.property.read
        using var client = CreateClientWithGroups("customer-care");
        var response = await client.GetAsync($"/api/properties/{propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Group 'customer-care' expands to roles that include 'catalog.property.read'");
    }

    // =========================================================================
    // D. DomainAction permission enforcement (RefundInvoiceAction)
    //    [RequirePermission("billing.invoice.refund")] — enforced at both
    //    endpoint level (PragmaticPermissionRequirement) and action pipeline
    //    level (PermissionAuthorizationFilter).
    //    Tests use a fake invoice ID since invoice creation via event handlers
    //    has a pre-existing DI issue (IDefaultValueGenerator not registered).
    // =========================================================================

    [Fact]
    public async Task RefundInvoice_WithoutPermission_Returns403()
    {
        // Use a non-existent invoice ID — permission check runs before entity lookup
        using var client = CreateClientWithPermissions(); // authenticated, no permissions
        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{Guid.NewGuid()}/refund",
            new { originalTransactionId = "txn-no-perm" },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "User without 'billing.invoice.refund' should be denied at endpoint level (before entity lookup)");
    }

    [Fact]
    public async Task RefundInvoice_WithPermission_BypassesEndpointAuth()
    {
        // Use a non-existent invoice ID — we're testing that the permission check PASSES,
        // not that the refund succeeds. A 404 or 500 (not 403) means auth was OK.
        using var client = CreateClientWithPermissions("billing.invoice.refund");
        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{Guid.NewGuid()}/refund",
            new { originalTransactionId = "txn-perm-test" },
            JsonOptions);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "User with 'billing.invoice.refund' should pass endpoint-level authorization");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "Authenticated user should not get 401");
    }

    // =========================================================================
    // E. Policy enforcement (CreateReservation with ReservationManagementPolicy)
    //    Policy: IsAuthenticated() & RequirePermission("booking.reservation.create")
    // =========================================================================

    [Fact]
    public async Task CreateReservation_WithBookingPermission_Returns201()
    {
        // Need all permissions for the prerequisite entities + reservation creation
        using var client = CreateClientWithPermissions("booking.reservation.create");
        var reservationId = await CreateFullReservationWithClientAsync(client);

        reservationId.Should().NotBeEmpty(
            "User with 'booking.reservation.create' should be able to create reservations via ReservationManagementPolicy");
    }

    [Fact]
    public async Task CreateReservation_WithoutPermission_ReturnsForbidden()
    {
        // Create prerequisites with default client (mutations don't enforce permissions)
        var (guestId, propertyId, roomTypeId) = await CreateReservationPrerequisitesAsync();

        // Try to create reservation with a client that has no permissions
        using var client = CreateClientWithPermissions(); // authenticated, no permissions
        var response = await PostWithClientAsync(client, "/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "ReservationManagementPolicy requires 'booking.reservation.create' permission");
    }

    // =========================================================================
    // F. Cross-boundary endpoint enforcement (GetReservationEndpoint)
    //    [RequirePermission("booking.reservation.read")]
    // =========================================================================

    [Fact]
    public async Task GetReservation_WithExactPermission_Returns200()
    {
        // Create reservation with default client (which is also the creator for row-level security)
        var reservationId = await CreateFullReservationAsync();

        // Access with explicit permission — same tenant, any user ID
        // booking.reservation.view-all bypasses [HasOwner] OwnershipFilter
        using var client = CreateClientWithPermissions("booking.reservation.read", "booking.reservation.view-all");
        var response = await client.GetAsync($"/api/reservations/{reservationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "User with 'booking.reservation.read' and 'booking.reservation.view-all' should access any reservation");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Creates a property using the default client (no permission enforcement on mutations).</summary>
    private async Task<Guid> CreatePropertyAsync()
    {
        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"PE-{Guid.NewGuid():N}"[..12],
            name = $"PermProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        return prop.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateFullReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Perm",
            lastName = "Test",
            email = $"perm.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"PE-{Guid.NewGuid():N}"[..12],
            name = $"PermProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Perm Room",
            code = "PMR",
            baseRate = 100m,
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
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    /// <summary>Creates prerequisites for a reservation (guest + property + room type).</summary>
    private async Task<(Guid guestId, Guid propertyId, Guid roomTypeId)> CreateReservationPrerequisitesAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Policy",
            lastName = "Test",
            email = $"policy.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"PL-{Guid.NewGuid():N}"[..12],
            name = $"PolicyProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Policy Room",
            code = "PLR",
            baseRate = 100m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    /// <summary>Creates a full reservation using a specific client (for permission testing).</summary>
    private async Task<Guid> CreateFullReservationWithClientAsync(HttpClient client)
    {
        // Prerequisites use default client (mutations don't enforce permissions at endpoint level)
        var (guestId, propertyId, roomTypeId) = await CreateReservationPrerequisitesAsync();

        // Create reservation with the specific client (policy enforcement happens here)
        var response = await PostWithClientAsync(client, "/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    /// <summary>
    ///     Creates a new HttpClient with specific groups via X-User-Groups header.
    ///     Permissions are resolved via group → role → permission expansion.
    /// </summary>
    private HttpClient CreateClientWithGroups(params string[] groups)
    {
        var client = CreateClientWithPermissions(); // base: authenticated, no permissions
        client.DefaultRequestHeaders.Remove("X-User-Groups"); // clean up if any
        if (groups.Length > 0)
            client.DefaultRequestHeaders.Add("X-User-Groups", string.Join(",", groups));
        return client;
    }
}
