using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR deletes a team, a kind of absence or an allowance that is no longer needed. What is
///     still in use is refused, by the rule the entities declare, and the refusal says what uses it.
/// </summary>
public sealed class DeletingWhatIsNoLongerNeeded(PostgresFixture database) : TimeOffTestBase(database)
{
    // =========================================================================
    // Teams — the members stay, without a team
    // =========================================================================

    [Fact]
    public async Task DeletingATeam_LeavesItsMembersWithoutOne_AndItsRequestsVisibleToHr()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var name = $"Team {Guid.NewGuid():N}"[..20];
        var teamId = (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name, managerId = manager.Id }))).GetProperty("id").GetGuid();
        var member = await HireAsync(teamId: teamId);
        var (kind, _) = await DefineKindAsync(hr, usesAllowance: false);
        var request = await AskForADayAsync(member, kind);

        // The control: before, the team is listed and the member reads back in it.
        (await TeamsNamedAsync(hr, name)).Should().Contain(teamId);
        (await EmployeeAsync(hr, member.Id)).GetProperty("teamId").GetGuid().Should().Be(teamId);

        var deleted = await hr.DeleteAsync($"/api/teams/{teamId}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        (await TeamsNamedAsync(hr, name)).Should().NotContain(teamId);
        var after = await EmployeeAsync(hr, member.Id);
        (after.TryGetProperty("teamId", out var teamAfter) ? teamAfter.ValueKind : JsonValueKind.Null)
            .Should().Be(JsonValueKind.Null, "deleting a team leaves its members without one (SetNull, as declared)");
        (await hr.GetAsync($"/api/leave-requests/{request}")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the request keeps the team's scope, and HR sees every request");
    }

    // =========================================================================
    // Kinds of absence — refused while anything uses them
    // =========================================================================

    [Fact]
    public async Task AnUnusedKind_IsDeleted()
    {
        var hr = await SignInAsHrAsync();
        var (kind, code) = await DefineKindAsync(hr);
        (await KindsCodedAsync(hr, code)).Should().Contain(kind, "the control: the read finds it before");

        var deleted = await hr.DeleteAsync($"/api/absence-kinds/{kind}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        (await KindsCodedAsync(hr, code)).Should().NotContain(kind);
    }

    [Fact]
    public async Task AKindAnAllowanceUses_IsRefused_AndStays()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var (kind, code) = await DefineKindAsync(hr);
        await GrantAsync(hr, employee.Id, kind);

        var refused = await hr.DeleteAsync($"/api/absence-kinds/{kind}");

        await ShouldBeInUseAsync(refused, entity: "AbsenceKind", usedBy: "Allowance");
        (await KindsCodedAsync(hr, code)).Should().Contain(kind);
    }

    [Fact]
    public async Task AKindARequestUses_IsRefused_AndStays()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var (kind, code) = await DefineKindAsync(hr, usesAllowance: false);
        await AskForADayAsync(employee, kind);

        var refused = await hr.DeleteAsync($"/api/absence-kinds/{kind}");

        await ShouldBeInUseAsync(refused, entity: "AbsenceKind", usedBy: "LeaveRequest");
        (await KindsCodedAsync(hr, code)).Should().Contain(kind);
    }

    // =========================================================================
    // Allowances — refused while a request draws on them
    // =========================================================================

    [Fact]
    public async Task AnAllowanceNothingDrawsOn_IsDeleted()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var (kind, _) = await DefineKindAsync(hr);
        var allowance = await GrantAsync(hr, employee.Id, kind);
        (await BalancesOfAsync(employee)).GetArrayLength().Should().Be(1, "the control: the balance reads it before");

        var deleted = await hr.DeleteAsync($"/api/allowances/{allowance}");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        (await BalancesOfAsync(employee)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AnAllowanceARequestDrawsOn_IsRefused_AndStays()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var (kind, _) = await DefineKindAsync(hr);
        var allowance = await GrantAsync(hr, employee.Id, kind);
        await AskForADayAsync(employee, kind);

        var refused = await hr.DeleteAsync($"/api/allowances/{allowance}");

        await ShouldBeInUseAsync(refused, entity: "Allowance", usedBy: "LeaveRequest");
        (await BalancesOfAsync(employee)).GetArrayLength().Should().Be(1);
    }

    // =========================================================================

    /// <summary>
    ///     A 409 with the framework's code for a row still in use, naming what is in use and what uses it —
    ///     read from the declared relation, not written by hand.
    /// </summary>
    private static async Task ShouldBeInUseAsync(HttpResponseMessage refused, string entity, string usedBy)
    {
        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, body);

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("ENTITY_IN_USE");
        problem.RootElement.GetProperty("entityType").GetString().Should().Be(entity);
        problem.RootElement.GetProperty("usedBy").GetString().Should().Be(usedBy);
    }

    private async Task<Guid> AskForADayAsync(HiredEmployee employee, Guid kind)
    {
        var session = await SignInAsync(employee.Account);
        var request = await ReadJsonAsync(await session.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from = Day(2), to = Day(2) }));
        return request.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> GrantAsync(HttpClient hr, Guid employeeId, Guid kind)
    {
        var allowance = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId, absenceKindId = kind, year = Year, entitled = 20m, carriedOver = 0m }));
        return allowance.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> BalancesOfAsync(HiredEmployee employee)
    {
        var session = await SignInAsync(employee.Account);
        return await ReadJsonAsync(await session.GetAsync($"/api/me/balances?year={Year}"));
    }

    private static async Task<JsonElement> EmployeeAsync(HttpClient hr, Guid id)
        => await ReadJsonAsync(await hr.GetAsync($"/api/employees/{id}"));

    /// <summary>A kind with a code of its own, so a read can look for exactly it.</summary>
    private static async Task<(Guid Id, string Code)> DefineKindAsync(HttpClient hr, bool usesAllowance = true)
    {
        var code = $"KIND_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var kind = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/absence-kinds", new
        {
            code,
            name = new Dictionary<string, string> { ["en-US"] = "A kind", ["it-IT"] = "Un tipo" },
            unit = "Days",
            usesAllowance
        }));
        return (kind.GetProperty("id").GetGuid(), code);
    }

    private static async Task<IReadOnlyList<Guid>> TeamsNamedAsync(HttpClient hr, string name)
    {
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/teams?name={Uri.EscapeDataString(name)}"));
        return [.. page.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];
    }

    private static async Task<IReadOnlyList<Guid>> KindsCodedAsync(HttpClient hr, string code)
    {
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/absence-kinds?code={code}"));
        return [.. page.GetProperty("items").EnumerateArray().Select(k => k.GetProperty("id").GetGuid())];
    }
}
