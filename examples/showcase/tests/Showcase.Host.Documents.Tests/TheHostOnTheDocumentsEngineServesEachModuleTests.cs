using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Documents.Tests;

/// <summary>
///     The Showcase host on the documents engine, started against MongoDB: one endpoint of each module answers from the database. What a
///     module writes is read back through the host and found in MongoDB by a client of the test's own, so neither an in-memory store nor a
///     500 passes.
/// </summary>
[Collection(DocumentsHostCollection.Name)]
public sealed class TheHostOnTheDocumentsEngineServesEachModuleTests(DocumentsHostFixture fixture)
{
    [Fact]
    public async Task Catalog_APropertyCreated_IsReadBack_AndIsADocumentOfShowcaseApp()
    {
        var code = $"DOC-{Guid.NewGuid():N}"[..12];
        var created = await Caller().PostAsJsonAsync("/api/properties", new { code, name = "Engine Hotel", city = "Rome", country = "IT", starRating = 3 });
        created.IsSuccessStatusCode.Should().BeTrue("the create runs through the Catalog boundary's session. Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var read = await Caller().GetAsync(new Uri($"/api/properties/{id}", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(code);

        (await Count("showcase_app", "Properties", "Code", code)).Should().Be(1);
    }

    [Fact]
    public async Task Booking_AGuestCreated_IsReadBack_AndIsADocumentOfShowcaseApp()
    {
        var email = $"guest.{Guid.NewGuid():N}@test.com";
        var created = await Caller().PostAsJsonAsync("/api/guests", new { firstName = "Engine", lastName = "Guest", email });
        created.IsSuccessStatusCode.Should().BeTrue("the create runs through the Booking boundary's session. Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var read = await Caller().GetAsync(new Uri($"/api/guests/{id}", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().Should().Be(email);

        (await Count("showcase_app", "Guests", "Email", email)).Should().Be(1);
    }

    /// <summary>
    ///     A reservation created through the host carries the number its <c>[GeneratedValue("RES-{YYYY}{MM}-{SEQ:5}")]</c> declares, counted in
    ///     MongoDB. The host registers the module's generated values although it finds no EF Core repository (#571): before, every number
    ///     stayed "".
    /// </summary>
    [Fact]
    public async Task Booking_AReservationCreated_IsNumberedByItsSequence()
    {
        var guest = await Caller().PostAsJsonAsync("/api/guests", new { firstName = "Engine", lastName = "Numbered", email = $"numbered.{Guid.NewGuid():N}@test.com" });
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var property = await Caller().PostAsJsonAsync("/api/properties", new { code = $"NUM-{Guid.NewGuid():N}"[..12], name = "Numbered Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roomType = await Caller().PostAsJsonAsync("/api/room-types", new { propertyId, name = "Numbered Room", code = "NUM", baseRate = 100m, totalRooms = 10 });
        roomType.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", roomType.StatusCode, await roomType.Content.ReadAsStringAsync());
        var roomTypeId = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var numberedBefore = await CountNumbered();

        var created = await Caller().PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"), checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"), numberOfGuests = 2 },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await created.Content.ReadAsStringAsync());

        (await CountNumbered()).Should().Be(numberedBefore + 1, "the reservation just created carries the declared format");
    }

    /// <summary>The reservations of showcase_app whose number has the declared format.</summary>
    private async Task<long> CountNumbered()
    {
        var reply = await fixture.Client.RunCommandAsync("showcase_app", w =>
        {
            w.Write("count", "Reservations");
            w.StartDocument("query");
            w.StartDocument("ReservationNumber");
            w.Write("$regex", @"^RES-\d{6}-\d{5}$");
            w.EndDocument();
            w.EndDocument();
        });
        reply.TryGetNumber("n", out var n).Should().BeTrue("the count command answers with n");
        return (long)n;
    }

    /// <summary>
    ///     A login checks the password against the account the registration stored: the wrong one is refused, so a login that succeeds read it
    ///     from the database.
    /// </summary>
    [Fact]
    public async Task Accounts_AUserRegistered_LogsInWithItsPassword_AndNotWithAnother()
    {
        var email = $"user.{Guid.NewGuid():N}@test.com";
        var registered = await Caller().PostAsJsonAsync("/identity/local/register", new { email, password = "Engine@Pass1" });
        registered.IsSuccessStatusCode.Should().BeTrue("the registration runs through the Accounts boundary's session. Got {0}: {1}", registered.StatusCode, await registered.Content.ReadAsStringAsync());

        var login = await Caller().PostAsJsonAsync("/identity/local/login", new { email, password = "Engine@Pass1" });
        login.IsSuccessStatusCode.Should().BeTrue("the account the registration stored is read back. Got {0}: {1}", login.StatusCode, await login.Content.ReadAsStringAsync());

        var wrong = await Caller().PostAsJsonAsync("/identity/local/login", new { email, password = "Another@Pass1" });
        wrong.IsSuccessStatusCode.Should().BeFalse("the control: a login that succeeded whatever the password would have read nothing");
    }

    /// <summary>Billing's search runs on showcase_financial, the database of its own, whose schema the host applied at start.</summary>
    [Fact]
    public async Task Billing_TheInvoiceSearch_AnswersFromShowcaseFinancial()
    {
        var search = await Caller().GetAsync(new Uri("/api/invoices/search", UriKind.Relative));
        search.StatusCode.Should().Be(HttpStatusCode.OK, "the search runs through the Billing boundary's session. Body: {0}", await search.Content.ReadAsStringAsync());

        var reply = await fixture.Client.RunCommandAsync("showcase_financial", w =>
        {
            w.Write("listCollections", 1);
            w.StartDocument("filter");
            w.Write("name", "Invoices");
            w.EndDocument();
        });
        var found = 0;
        await using (var cursor = fixture.Client.Cursor("showcase_financial", reply))
            await foreach (var _ in cursor)
                found++;
        found.Should().Be(1, "the host applied Billing's documents schema to its database at start");
    }

    /// <summary>A caller the host accepts: a tenant, an identity and every permission.</summary>
    private HttpClient Caller()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "engine-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Documents host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        return client;
    }

    private async Task<long> Count(string database, string collection, string field, string value)
    {
        var reply = await fixture.Client.RunCommandAsync(database, w =>
        {
            w.Write("count", collection);
            w.StartDocument("query");
            w.Write(field, value);
            w.EndDocument();
        });
        reply.TryGetNumber("n", out var n).Should().BeTrue("the count command answers with n");
        return (long)n;
    }
}
