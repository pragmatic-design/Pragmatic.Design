using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     At the start of a year HR grants a whole team its allowance of a kind in one operation,
///     and it is all or nothing.
/// </summary>
public sealed class GrantingATeamItsAllowances(PostgresFixture database) : TimeOffTestBase(database)
{
    private const int Year = 2026;

    [Fact]
    public async Task ATeamOfThree_GetsThreeAllowances()
    {
        var team = await ATeamAsync(members: 3);

        var granted = await ReadJsonAsync(await team.Hr.PostAsJsonAsync($"/api/teams/{team.Id}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 26m }));

        granted.EnumerateArray().Select(a => a.GetProperty("employeeId").GetGuid())
            .Should().BeEquivalentTo(team.Members.Select(m => m.Id));
        granted.EnumerateArray().Should().OnlyContain(a =>
            a.GetProperty("entitled").GetDecimal() == 26m && a.GetProperty("year").GetInt32() == Year);
        foreach (var member in team.Members)
            (await AllowancesOfAsync(member, team.Kind)).Should().Be(1, $"{member.Account.FullName} was granted one");
    }

    /// <summary>
    ///     One operation, one write: the grants are staged by the mutations the action invokes and
    ///     written together when the action commits — not each on its own, which is what would let one
    ///     fail while the others stayed.
    /// </summary>
    [Fact]
    public async Task TheGrants_AreWrittenTogether()
    {
        var team = await ATeamAsync(members: 3);

        Sql.Clear();
        await ReadJsonAsync(await team.Hr.PostAsJsonAsync($"/api/teams/{team.Id}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 26m }));

        Sql.Commands.Where(c => c.Contains("INSERT INTO", StringComparison.Ordinal)
                                && c.Contains("\"Allowances\"", StringComparison.Ordinal))
            .Should().HaveCount(1, "the three grants are written by the one save that closes the operation");
    }

    [Fact]
    public async Task WithOneMemberAlreadyGranted_TheAnswerIs409NamingThem_AndNothingIsCreated()
    {
        var team = await ATeamAsync(members: 3);
        var already = team.Members[1];
        await ReadJsonAsync(await team.Hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = already.Id, absenceKindId = team.Kind, year = Year, entitled = 20m }));
        var before = await AllowancesOfTeamAsync(team);

        var refused = await team.Hr.PostAsJsonAsync($"/api/teams/{team.Id}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 26m });

        var body = await refused.Content.ReadAsStringAsync();
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("ALLOWANCE_ALREADY_GRANTED");
        problem.RootElement.GetProperty("employeeNumbers").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(await EmployeeNumberOfAsync(team.Hr, already));

        var after = await AllowancesOfTeamAsync(team);
        after.Should().Equal(before, "all or nothing: the two members without one were not granted one either");
        before.Should().Equal(0, 1, 0);
    }

    [Fact]
    public async Task AnEmptyTeam_IsAnEmptyAnswer()
    {
        var team = await ATeamAsync(members: 0);

        var granted = await ReadJsonAsync(await team.Hr.PostAsJsonAsync($"/api/teams/{team.Id}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 26m }));

        granted.GetArrayLength().Should().Be(0);
    }

    /// <summary>The operation's own 404, with its code — not the one a route nobody mapped answers.</summary>
    [Fact]
    public async Task ATeamThatDoesNotExist_IsNotFound()
    {
        var team = await ATeamAsync(members: 0);

        var response = await team.Hr.PostAsJsonAsync($"/api/teams/{Guid.NewGuid()}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 26m });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("\"NOT_FOUND\"");
    }

    [Fact]
    public async Task AnEmployee_IsRefused()
    {
        var team = await ATeamAsync(members: 1);
        var employee = await SignInAsync(team.Members[0].Account);

        var response = await employee.PostAsJsonAsync($"/api/teams/{team.Id}/allowances",
            new { absenceKindId = team.Kind, year = Year, entitled = 365m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AllowancesOfAsync(team.Members[0], team.Kind)).Should().Be(0);
    }

    /// <summary>A team with a manager who is not one of its members, the members, and a new kind.</summary>
    private async Task<Team> ATeamAsync(int members)
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var teamId = (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }))).GetProperty("id").GetGuid();

        var hired = new List<HiredEmployee>();
        for (var i = 0; i < members; i++)
            hired.Add(await HireAsync(teamId: teamId));

        return new Team(hr, teamId, hired, await DefineKindAsync());
    }

    /// <summary>How many allowances of the kind the employee holds for the year, as they see it.</summary>
    private async Task<int> AllowancesOfAsync(HiredEmployee employee, Guid kind)
    {
        var session = await SignInAsync(employee.Account);
        var balances = await ReadJsonAsync(await session.GetAsync($"/api/me/balances?year={Year}"));
        return balances.EnumerateArray().Count(b => b.GetProperty("absenceKindId").GetGuid() == kind);
    }

    private async Task<int[]> AllowancesOfTeamAsync(Team team)
    {
        var counts = new List<int>();
        foreach (var member in team.Members)
            counts.Add(await AllowancesOfAsync(member, team.Kind));
        return [.. counts];
    }

    private static async Task<string> EmployeeNumberOfAsync(HttpClient hr, HiredEmployee employee)
        => (await ReadJsonAsync(await hr.GetAsync($"/api/employees/{employee.Id}")))
            .GetProperty("employeeNumber").GetString()!;

    private sealed record Team(HttpClient Hr, Guid Id, IReadOnlyList<HiredEmployee> Members, Guid Kind);
}
