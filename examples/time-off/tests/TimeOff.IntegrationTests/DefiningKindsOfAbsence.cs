using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR defines the kinds of absence, each named in Italian and English and counted by a
///     rule of its own.
/// </summary>
public sealed class DefiningKindsOfAbsence(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task AnEmployee_ReadsTheNameOfAKind_InTheLanguageOfTheRequest()
    {
        var hr = await SignInAsHrAsync();
        var code = $"VACATION_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/absence-kinds", new
        {
            code,
            name = new Dictionary<string, string> { ["en-US"] = "Vacation", ["it-IT"] = "Ferie" },
            unit = "Days",
            usesAllowance = true
        }));
        var employee = await SignInAsync((await HireAsync()).Account);

        (await NameOf(employee, code, "it-IT")).Should().Be("Ferie");
        (await NameOf(employee, code, "en-US")).Should().Be("Vacation");
    }

    /// <summary>
    ///     Hours are only ever granted as a budget; a kind counted in hours with no allowance behind it
    ///     would bound nothing.
    /// </summary>
    [Fact]
    public async Task AKindCountedInHours_WithoutAnAllowance_IsRefusedWith422_NamingTheField()
    {
        var hr = await SignInAsHrAsync();

        var response = await hr.PostAsJsonAsync("/api/absence-kinds", new
        {
            code = $"HOURS_{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = new Dictionary<string, string> { ["en-US"] = "Unbounded hours", ["it-IT"] = "Ore senza limite" },
            unit = "Hours",
            usesAllowance = false
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        body.Should().Contain("usesAllowance", "the error names the field the rule is about");
    }

    [Fact]
    public async Task AKindCountedInHours_FromAnAllowance_IsAccepted()
    {
        var hr = await SignInAsHrAsync();

        var created = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/absence-kinds", new
        {
            code = $"PERSONAL_{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            name = new Dictionary<string, string> { ["en-US"] = "Personal hours", ["it-IT"] = "Permessi" },
            unit = "Hours",
            usesAllowance = true
        }));

        created.GetProperty("unit").GetString().Should().Be("Hours");
    }

    [Fact]
    public async Task AnEmployee_CannotDefineKinds()
    {
        var employee = await SignInAsync((await HireAsync()).Account);

        var response = await employee.PostAsJsonAsync("/api/absence-kinds", new
        {
            code = "SNEAKY",
            name = new Dictionary<string, string> { ["en-US"] = "Sneaky" },
            unit = "Days",
            usesAllowance = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<string?> NameOf(HttpClient client, string code, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/absence-kinds?code={code}");
        request.Headers.AcceptLanguage.ParseAdd(culture);
        var page = await ReadJsonAsync(await client.SendAsync(request));

        return Items(page).Single(kind => kind.GetProperty("code").GetString() == code)
            .GetProperty("name").GetString();
    }

    private static IEnumerable<JsonElement> Items(JsonElement page) =>
        page.ValueKind == JsonValueKind.Array ? page.EnumerateArray() : page.GetProperty("items").EnumerateArray();
}
