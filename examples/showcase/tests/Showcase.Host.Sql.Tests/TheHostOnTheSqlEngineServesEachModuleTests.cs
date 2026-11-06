using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     The Showcase host on the Sql engine, started against PostgreSQL: one endpoint of each module answers from the database. What a module
///     writes is read back through the host and found in PostgreSQL by a connection of the test's own, so neither an in-memory store nor a
///     500 passes.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class TheHostOnTheSqlEngineServesEachModuleTests(SqlHostFixture fixture)
{
    [Fact]
    public async Task Catalog_APropertyCreated_IsReadBack_AndIsARowOfShowcaseApp()
    {
        var code = $"SQL-{Guid.NewGuid():N}"[..12];
        var created = await Caller().PostAsJsonAsync("/api/properties", new { code, name = "Engine Hotel", city = "Rome", country = "IT", starRating = 3 });
        created.IsSuccessStatusCode.Should().BeTrue("the create runs through the Catalog boundary's session. Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var read = await Caller().GetAsync(new Uri($"/api/properties/{id}", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be(code);

        (await Count(fixture.AppConnectionString, "SELECT COUNT(*) FROM \"Properties\" WHERE \"Code\" = @value", code)).Should().Be(1);
    }

    [Fact]
    public async Task Booking_AGuestCreated_IsReadBack_AndIsARowOfShowcaseApp()
    {
        var email = $"guest.{Guid.NewGuid():N}@test.com";
        var created = await Caller().PostAsJsonAsync("/api/guests", new { firstName = "Engine", lastName = "Guest", email });
        created.IsSuccessStatusCode.Should().BeTrue("the create runs through the Booking boundary's session. Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var read = await Caller().GetAsync(new Uri($"/api/guests/{id}", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString().Should().Be(email);

        (await Count(fixture.AppConnectionString, "SELECT COUNT(*) FROM \"Guests\" WHERE \"Email\" = @value", email)).Should().Be(1);
    }

    /// <summary>
    ///     The search a <c>[Resource]</c> scaffolds sorts by <c>Id</c> by default: on the Sql engine the key's column is <c>PersistenceId</c>, and
    ///     a statement ordered by <c>t."Id"</c> answered 500 on every search (#564).
    /// </summary>
    [Fact]
    public async Task Booking_TheGuestSearch_FindsTheGuestCreated()
    {
        var lastName = $"Search{Guid.NewGuid():N}"[..14];
        var created = await Caller().PostAsJsonAsync("/api/guests", new { firstName = "Engine", lastName, email = $"{lastName}@test.com" });
        created.IsSuccessStatusCode.Should().BeTrue("Got {0}: {1}", created.StatusCode, await created.Content.ReadAsStringAsync());

        var search = await Caller().GetAsync(new Uri($"/api/booking/guests/search?lastName={lastName}", UriKind.Relative));
        search.StatusCode.Should().Be(HttpStatusCode.OK, "the scaffolded search orders by the key's column. Body: {0}", await search.Content.ReadAsStringAsync());
        var items = (await search.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        items.GetArrayLength().Should().Be(1, "the guest just created is the only one of that last name");
    }

    /// <summary>
    ///     A reservation created through the host carries the number its <c>[GeneratedValue("RES-{YYYY}{MM}-{SEQ:5}")]</c> declares, read from
    ///     PostgreSQL. The host registers the module's generated values although it finds no EF Core repository (#571): before, the binding
    ///     list was empty, the number stayed "", and the second invoice of the day violated its unique index.
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

        var created = await Caller().PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"), checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"), numberOfGuests = 2 },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();

        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"ReservationNumber\" FROM \"Reservations\" WHERE \"PersistenceId\" = @id", connection);
        command.Parameters.AddWithValue("id", id);
        var number = (string?)await command.ExecuteScalarAsync();
        number.Should().MatchRegex(@"^RES-\d{6}-\d{5}$", "the row carries the declared format");
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

    /// <summary>Billing's search runs on showcase_financial, the database of its own, migrated at start.</summary>
    [Fact]
    public async Task Billing_TheInvoiceSearch_AnswersFromShowcaseFinancial()
    {
        var search = await Caller().GetAsync(new Uri("/api/invoices/search", UriKind.Relative));
        search.StatusCode.Should().Be(HttpStatusCode.OK, "the search runs through the Billing boundary's session. Body: {0}", await search.Content.ReadAsStringAsync());

        (await Count(fixture.FinancialConnectionString, "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @value", "Invoices"))
            .Should().Be(1, "the host migrated Billing's database to its schema at start");
    }

    /// <summary>A caller the host accepts: a tenant, an identity and every permission.</summary>
    private HttpClient Caller()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "engine-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Sql host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        return client;
    }

    private static async Task<long> Count(string connectionString, string sql, string value)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
