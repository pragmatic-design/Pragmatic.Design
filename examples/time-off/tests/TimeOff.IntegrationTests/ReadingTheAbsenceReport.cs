using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR reads the days of absence of a year by team, kind and month: one line per group,
///     the approved amount and the number of requests, added up by the database.
/// </summary>
public sealed class ReadingTheAbsenceReport(PostgresFixture database) : TimeOffTestBase(database)
{
    /// <summary>
    ///     Two teams, two kinds — one counted in days, one in hours — and two months; approved, pending
    ///     and rejected requests. Only the approved ones count, each in the month it starts.
    /// </summary>
    [Fact]
    public async Task TheReport_SumsTheApprovedRequests_ByTeamKindAndMonth()
    {
        var hr = await SignInAsHrAsync();
        var (teamA, managerA, memberA) = await ATeamWithAMemberAsync(hr);
        var (teamB, managerB, memberB) = await ATeamWithAMemberAsync(hr);
        var vacation = await DefineKindAsync();
        var hours = await DefineKindAsync(unit: "Hours");
        await GrantAsync(hr, memberA.Id, vacation, 20m);
        await GrantAsync(hr, memberB.Id, vacation, 20m);
        await GrantAsync(hr, memberA.Id, hours, 40m);

        var a = await SignInAsync(memberA.Account);
        var b = await SignInAsync(memberB.Account);

        await ApproveAsync(managerA, await AskAsync(a, vacation, Day(0), Day(1)));             // 2 days, February
        await ApproveAsync(managerA, await AskAsync(a, vacation, Day(28), Day(28)));           // 1 day, March
        await AskAsync(a, vacation, Day(2), Day(2));                                           // pending: not counted
        await RejectAsync(managerA, await AskAsync(a, vacation, Day(3), Day(3)));              // rejected: not counted
        await ApproveAsync(managerA, await AskAsync(a, hours, Day(4), Day(4), hoursOnTheDay: 3m)); // 3 hours, February
        await ApproveAsync(managerB, await AskAsync(b, vacation, Day(0), Day(0)));             // 1 day, February
        await ApproveAsync(managerB, await AskAsync(b, vacation, LastWorkingDayOfFebruary(), FirstWorkingDayOfMarch())); // 2 days, counted in February

        Sql.Clear();
        var report = await ReadJsonAsync(await hr.GetAsync($"/api/reports/absences?year={Year}"));

        var ours = report.EnumerateArray()
            .Where(line => line.TryGetProperty("teamId", out var team) && team.ValueKind == JsonValueKind.String
                           && (team.GetGuid() == teamA || team.GetGuid() == teamB))
            .Select(line => (
                Team: line.GetProperty("teamId").GetGuid(),
                Kind: line.GetProperty("absenceKindId").GetGuid(),
                Month: line.GetProperty("startMonth").GetInt32(),
                Unit: line.GetProperty("unit").GetString(),
                Taken: line.GetProperty("taken").GetDecimal(),
                Requests: line.GetProperty("requests").GetInt32()))
            .OrderBy(line => line.Team == teamA ? 0 : 1).ThenBy(line => line.Kind == vacation ? 0 : 1).ThenBy(line => line.Month)
            .ToList();

        ours.Should().Equal(
            (teamA, vacation, 2, "Days", 2m, 1),
            (teamA, vacation, 3, "Days", 1m, 1),
            (teamA, hours, 2, "Hours", 3m, 1),
            (teamB, vacation, 2, "Days", 3m, 2));

        var reads = Sql.Commands.Where(c => c.Contains("\"LeaveRequests\"", StringComparison.Ordinal)).ToList();
        reads.Should().ContainSingle("the report is one query");
        reads[0].Should().MatchRegex("(?i)\\bgroup by\\b").And.MatchRegex("(?i)\\bsum\\(",
            "the database adds the requests up, not the application");
    }

    [Fact]
    public async Task AnEmployee_CannotReadTheReport()
    {
        var employee = await SignInAsync((await HireAsync()).Account);

        (await employee.GetAsync($"/api/reports/absences?year={Year}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>A request that starts in February and ends in March: two working days, the weekend between.</summary>
    private static string LastWorkingDayOfFebruary()
    {
        var day = new DateOnly(Year, 3, 1).AddDays(-1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            day = day.AddDays(-1);
        return day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FirstWorkingDayOfMarch()
    {
        var day = new DateOnly(Year, 3, 1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            day = day.AddDays(1);
        return day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<(Guid Team, HttpClient Manager, HiredEmployee Member)> ATeamWithAMemberAsync(HttpClient hr)
    {
        var manager = await HireAsync(role: "Manager");
        var team = (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }))).GetProperty("id").GetGuid();
        var member = await HireAsync(teamId: team);
        return (team, await SignInAsync(manager.Account), member);
    }

    private static async Task GrantAsync(HttpClient hr, Guid employeeId, Guid kind, decimal entitled)
        => await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId, absenceKindId = kind, year = Year, entitled, carriedOver = 0m }));

    private static async Task<Guid> AskAsync(HttpClient employee, Guid kind, string from, string to, decimal? hoursOnTheDay = null)
    {
        var request = await ReadJsonAsync(await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to, hours = hoursOnTheDay }));
        return request.GetProperty("id").GetGuid();
    }

    private static async Task ApproveAsync(HttpClient manager, Guid request)
        => await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));

    private static async Task RejectAsync(HttpClient manager, Guid request)
        => await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{request}/reject", new { }));
}
