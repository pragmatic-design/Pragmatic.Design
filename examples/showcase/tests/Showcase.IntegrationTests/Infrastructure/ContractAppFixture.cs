using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing;

namespace Showcase.IntegrationTests.Infrastructure;

// #7 E2E wiring (escape hatch): the contract-test generator emits test classes that read their HttpClient from
// PragmaticContractHost and all share the "PragmaticContractTests" xUnit collection. The consumer supplies that
// collection's fixture once — here it boots the real showcase app over Testcontainers PostgreSQL (reusing the
// existing PostgresFixture + ShowcaseWebFactory) and assigns the client. No per-generated-class wiring needed.

/// <summary>Boots the showcase app over a throwaway PostgreSQL container and publishes the client for the generated tests.</summary>
public sealed class ContractAppFixture : IAsyncLifetime
{
    private readonly PostgresFixture _db = new();
    private ShowcaseWebFactory? _factory;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _factory = new ShowcaseWebFactory(_db);

        // The generated contract tests are positive/authorized contracts (unknown-id → 404, valid body →
        // 201). The app is multi-tenant (RequireTenant) and endpoints are permission-gated, so the shared
        // contract client runs as an authorized tenant user with a full-access "*" grant. (Denial contracts,
        // if any, would supply their own unauthenticated client.)
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "contract-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "contract-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Contract Test User");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        PragmaticContractHost.Client = client;

        // A reservation is created for a guest, at a property, in a room type — three rows that must
        // already exist. The generator fills a body from the operation's shape and cannot invent them, so
        // the application supplies the body it considers valid. Seeded once, here, because the hook is
        // synchronous and this is the only place that can await.
        var (guestId, propertyId, roomTypeId) = await SeedReservationPrerequisitesAsync(client);

        // The same reason, for three creates whose required member is a foreign key: a room type, a
        // cancellation policy and a staff assignment all hang off a property that must exist. The
        // generator cannot fill their bodies, and it still emits their contract tests and asks for one.
        //
        // ⚠️ A property of its own for the staff assignment: [TemporalRelation<Property>(MaxActive = 1)]
        // allows one active assignment per property, so sharing the reservation's property would make this
        // body a 409 the day something else assigns somebody to it.
        var staffPropertyId = await CreatedIdAsync(client, "/api/properties", AProperty());

        PragmaticContractHost.BodyFor = operation => operation switch
        {
            // The constant, not a second copy of the string: the two drifted apart the moment the key
            // changed, and the arm stopped answering while the request preparation below still matched.
            CreateReservation => new { Request = AReservation(guestId, propertyId, roomTypeId) },
            "CreateRoomTypeMutation" => new
            {
                PropertyId = propertyId,
                Name = "Contract Room Type",
                Code = $"CT{Random.Shared.Next(1000, 9999)}",
                BaseRate = 120m,
                TotalRooms = 10
            },
            "CreateCancellationPolicyMutation" => new
            {
                PropertyId = propertyId,
                Name = $"Contract Policy {Guid.NewGuid():N}"[..24],
                HoursBeforeCheckIn = 48,
                PenaltyPercentage = 25m
            },
            "CreateStaffAssignmentMutation" => new
            {
                StaffId = Guid.NewGuid(),
                PropertyId = staffPropertyId,
                Role = "Concierge"
            },
            _ => null
        };

        // An invoice is never created over HTTP: confirming a reservation raises it. The transition contracts
        // of Invoice need one in its initial state, and only this application knows how one comes to be.
        PragmaticContractHost.ArrangeFor = async (entity, arrangeClient) => entity switch
        {
            "Invoice" => (await AnInvoiceAsync(arrangeClient, guestId, propertyId, roomTypeId)).ToString(),
            _ => null
        };

        // What the generator cannot know about this host, for the two requests of the reservation
        // transition contract: which API version to ask for, which authority the create needs, and that
        // both requests must be the same person.
        PragmaticContractHost.PrepareRequest = contract =>
        {
            var (_, operation, request) = contract;

            // An invoice carries the access scope of whoever raised it — here the arrangement, by confirming a
            // reservation — so the flow's own identity would not see it and the payment would be a 404. Whoever
            // records payments sees every invoice: that is the authority this request needs, beside its own.
            if (operation == PayInvoice)
            {
                var permissions = request.Headers.TryGetValues(PragmaticTestIdentity.PermissionsHeader, out var values)
                    ? string.Join(",", values)
                    : string.Empty;
                request.Headers.Remove(PragmaticTestIdentity.PermissionsHeader);
                request.Headers.Add(PragmaticTestIdentity.PermissionsHeader,
                    string.Join(",", new[] { permissions, "billing.invoice.view-all" }.Where(p => p.Length > 0)));
                return;
            }

            if (operation is not (CreateReservation or ConfirmReservation))
                return;

            // ⚠️ One caller for both, and it is the generated one: the reservation is visible to the
            // user who created it, so a create and a transition made by two different identities end
            // in a 404 on an entity that exists. Both requests carry the same generated id, so nothing
            // has to be substituted here.
            // ⚠️ No id of its own is needed. The generated identity is "contract-{permission}", which
            // the auth contracts also use with that one permission; the permission cache key carries the
            // trusted claims as well as the user id, so whichever contract arrives first does not decide
            // what the id can do for the rest of the run.

            // ⚠️ The authority. The create is gated by ReservationManagementPolicy, a class the
            // generator cannot read — so the create request goes out carrying nothing, and the policy
            // asks for booking.reservation.create. Each request carries the permission
            // its own endpoint declares, and this one declares none: what a policy requires is code,
            // not metadata, and only the application can say it.
            request.Headers.Remove(PragmaticTestIdentity.PermissionsHeader);
            request.Headers.Add(PragmaticTestIdentity.PermissionsHeader,
                "booking.reservation.create,booking.reservation.update");

            // ⚠️ The create is versioned — CreateReservationAction declares a V2 — and this app refuses a
            // request that does not say which version it wants ("An API version is required, but was not
            // specified"). The generator emits the route from the endpoint's own attribute and knows
            // nothing about the host's version reader.
            if (operation == CreateReservation)
                request.RequestUri = WithApiVersion(request.RequestUri!, "1.0");
        };
    }

    /// <summary>The generated contract's own names for the two requests of the reservation transition.</summary>
    private const string CreateReservation = "CreateReservationAction";

    private const string ConfirmReservation = "Reservation_TransitionToConfirmed";

    /// <summary>The generated contract's name for the payment of an arranged invoice.</summary>
    private const string PayInvoice = "Invoice_TransitionToPaid";

    /// <summary>The same URI, asking for a named API version. Relative, as the generated tests build it.</summary>
    private static Uri WithApiVersion(Uri uri, string version)
    {
        var text = uri.ToString();
        var separator = text.Contains('?') ? "&" : "?";
        return new Uri(text + separator + "api-version=" + version, UriKind.RelativeOrAbsolute);
    }

    /// <summary>Creates the guest, property and room type a reservation needs, over the API itself.</summary>
    private static async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)>
        SeedReservationPrerequisitesAsync(HttpClient client)
    {
        var guest = await CreatedIdAsync(client, "/api/guests", new
        {
            firstName = "Contract",
            lastName = "Guest",
            email = $"contract.{Guid.NewGuid():N}@example.com"
        });

        var property = await CreatedIdAsync(client, "/api/properties", AProperty());

        // Room enough that the availability validator never refuses the contract's reservation.
        var roomType = await CreatedIdAsync(client, "/api/room-types", new
        {
            propertyId = property,
            name = "Contract Room",
            code = "CTR",
            baseRate = 100m,
            totalRooms = 50
        });

        return (guest, property, roomType);
    }

    private static int _stays;

    /// <summary>
    ///     A reservation body for the seeded guest, in a stay no other reservation of this run overlaps.
    /// </summary>
    /// <remarks>
    ///     ⚠️ One guest, many reservations: a transition contract that walks, and the invoice arrangement,
    ///     each create one, and the same dates twice are refused ("guest already booked") before the
    ///     contract reaches the transition it exists for.
    /// </remarks>
    private static object AReservation(Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var checkIn = DateTimeOffset.UtcNow.Date.AddDays(7 + (Interlocked.Increment(ref _stays) * 5));
        return new
        {
            guestId,
            propertyId,
            roomTypeId,
            checkIn = new DateTimeOffset(checkIn, TimeSpan.Zero).ToString("O"),
            checkOut = new DateTimeOffset(checkIn.AddDays(3), TimeSpan.Zero).ToString("O"),
            numberOfGuests = 2
        };
    }

    /// <summary>The invoice a confirmed reservation raises, found through the search route.</summary>
    private static async Task<Guid> AnInvoiceAsync(HttpClient client, Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var created = await client.PostAsJsonAsync("/api/reservations?api-version=1.0",
            new { request = AReservation(guestId, propertyId, roomTypeId) }, SeedJson);
        created.EnsureSuccessStatusCode();
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(SeedJson);

        (await client.PostAsJsonAsync($"/api/reservations/{reservationId}/confirm", new { }, SeedJson))
            .EnsureSuccessStatusCode();

        var search = await client.GetAsync($"/api/invoices/search?reservationId={reservationId}");
        search.EnsureSuccessStatusCode();
        var items = (await search.Content.ReadFromJsonAsync<JsonElement>(SeedJson)).GetProperty("items");
        if (items.GetArrayLength() != 1)
            throw new InvalidOperationException(
                $"Confirming reservation {reservationId} raised {items.GetArrayLength()} invoices, not one.");

        return items[0].GetProperty("id").GetGuid();
    }

    /// <summary>A property body this app accepts, unique per call: the code and the name are unique.</summary>
    private static object AProperty() => new
    {
        code = $"CT-{Guid.NewGuid():N}"[..12],
        name = $"CTProp-{Guid.NewGuid():N}"[..20],
        city = "Rome",
        country = "IT",
        starRating = 3
    };

    private static async Task<Guid> CreatedIdAsync(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync(route, body, SeedJson);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(SeedJson);
        return created.GetProperty("id").GetGuid();
    }

    private static readonly JsonSerializerOptions SeedJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task DisposeAsync()
    {
        PragmaticContractHost.BodyFor = null;
        PragmaticContractHost.PrepareRequest = null;
        PragmaticContractHost.ArrangeFor = null;
        _factory?.Dispose();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractAppFixture>;
