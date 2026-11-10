using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A query in Booking bound to the caller with <c>[FromCurrentUser(nameof(AppUser.Id))]</c>, while the user lives in Accounts: it
///     answers the caller's own rows (#636).
/// </summary>
/// <remarks>
///     <para>
///         Two users, one assignment each, both read back through the same path: a query that bound nothing, or the first user, would give
///         one of them the other's row or none.
///     </para>
///     <para>
///         Everything goes through the host's endpoints, so the same test runs on every engine the Showcase is built on.
///     </para>
/// </remarks>
public sealed class MyPlaceOnTheRotaTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EachCaller_ReadsTheirOwnAssignments()
    {
        var ada = await RegisterAsync();
        var grace = await RegisterAsync();
        var adaAssignment = await AssignAsync(ada.UserId);
        var graceAssignment = await AssignAsync(grace.UserId);

        var adas = await MineAsync(CreateClientForIdentity(ada.Key), "");
        var graces = await MineAsync(CreateClientForIdentity(grace.Key), "");

        adas.Should().Equal(adaAssignment);
        graces.Should().Equal(graceAssignment);
    }

    /// <summary>The control: the value is the caller's, whatever the request says — a staff id on the URL reads nobody else's rota.</summary>
    [Fact]
    public async Task AStaffIdOnTheRequest_IsNotRead()
    {
        var ada = await RegisterAsync();
        var grace = await RegisterAsync();
        var adaAssignment = await AssignAsync(ada.UserId);
        await AssignAsync(grace.UserId);

        var adas = await MineAsync(CreateClientForIdentity(ada.Key), $"?staffId={grace.UserId}");

        adas.Should().Equal(adaAssignment);
    }

    /// <summary>Registers a user through the host and returns their external key and the id of their account, read as them.</summary>
    private async Task<(string Key, Guid UserId)> RegisterAsync()
    {
        var email = $"rota-{Guid.NewGuid():N}@test.com";
        var register = await PostAsync("/identity/local/register", new { email, password = "Rota@Pass1" });
        register.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);

        var key = (await register.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetString()
            ?? throw new InvalidOperationException("register returned no external identity key");

        // The default grant has no Accounts permission, and reading one's own account asks for it.
        var asThemselves = CreateClientAsWithPermissions(key, "Rota user", "accounts.app-user.read");
        asThemselves.DefaultRequestHeaders.Add("X-User-External-Key", key);
        var me = await asThemselves.GetAsync("api/accounts/me");
        var body = await me.Content.ReadAsStringAsync();
        me.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", body);
        return (key, JsonSerializer.Deserialize<JsonElement>(body, JsonOptions).GetProperty("id").GetGuid());
    }

    /// <summary>An assignment of <paramref name="staffId" /> on a property of its own.</summary>
    private async Task<Guid> AssignAsync(Guid staffId)
    {
        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"MR-{Guid.NewGuid():N}"[..12],
            name = $"Mine-{Guid.NewGuid():N}"[..20],
            city = "Turin",
            country = "IT",
            starRating = 4,
        });

        var assignment = await PostAsync<JsonElement>("/api/staff-assignments", new
        {
            staffId,
            propertyId = property.GetProperty("id").GetGuid(),
            role = "Concierge",
        });

        return assignment.GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> MineAsync(HttpClient client, string queryString)
    {
        var response = await client.GetAsync($"api/staff-assignments/mine{queryString}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", body);
        return JsonSerializer.Deserialize<List<JsonElement>>(body, JsonOptions)!.Select(a => a.GetProperty("id").GetGuid()).ToList();
    }
}
