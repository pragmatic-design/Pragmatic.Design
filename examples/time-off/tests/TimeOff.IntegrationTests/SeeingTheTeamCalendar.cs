using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     A manager sees who in the team is away in a period, by kind, in date order, a page at a
///     time; nobody outside the team.
/// </summary>
/// <remarks>
///     One team, built once: Ada away on vacation Mon–Wed, Ben on sick leave on Tuesday, Cleo on
///     vacation the week after and with a request still pending in the week; and Otto, in another team,
///     away on Monday. The week asked for is Mon–Fri of the first week of February next year
///     (<see cref="TestCalendar" />).
/// </remarks>
public sealed class SeeingTheTeamCalendar(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task TheManager_SeesTheTeamAwayInThePeriod_InDateOrder_AndNobodyElse()
    {
        var team = await ATeamWithAbsencesAsync();

        var page = await ReadJsonAsync(await team.Manager.GetAsync(CalendarOf(Day(0), Day(4))));

        var away = page.GetProperty("items").EnumerateArray().ToList();
        away.Select(a => a.GetProperty("employeeId").GetGuid()).Should().Equal(team.Ada, team.Ben);
        away.Select(a => a.GetProperty("from").GetString()).Should().Equal(Day(0), Day(1));
        away[0].GetProperty("employeeFullName").GetString().Should().StartWith("Employee ");
        page.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task TheCalendar_ComesAPageAtATime()
    {
        var team = await ATeamWithAbsencesAsync();

        var first = await ReadJsonAsync(await team.Manager.GetAsync(CalendarOf(Day(0), Day(4)) + "&page=1&pageSize=1"));
        var second = await ReadJsonAsync(await team.Manager.GetAsync(CalendarOf(Day(0), Day(4)) + "&page=2&pageSize=1"));

        IdsOf(first).Should().Equal(team.Ada);
        IdsOf(second).Should().Equal(team.Ben);
        first.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task TheKindOfAbsence_NarrowsIt()
    {
        var team = await ATeamWithAbsencesAsync();

        var page = await ReadJsonAsync(await team.Manager.GetAsync(
            CalendarOf(Day(0), Day(4)) + $"&absenceKindId={team.SickLeave}"));

        IdsOf(page).Should().Equal(team.Ben);
    }

    /// <summary>The control: the week after holds Cleo's approved vacation, which the first week did not show.</summary>
    [Fact]
    public async Task ThePeriod_DecidesWhoIsAway()
    {
        var team = await ATeamWithAbsencesAsync();

        var page = await ReadJsonAsync(await team.Manager.GetAsync(CalendarOf(Day(7), Day(11))));

        IdsOf(page).Should().Equal(team.Cleo);
    }

    private static string CalendarOf(string from, string to) => $"/api/team-calendar?from={from}&to={to}";

    private static IEnumerable<Guid> IdsOf(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("employeeId").GetGuid());

    private async Task<TeamWithAbsences> ATeamWithAbsencesAsync()
    {
        var hr = await SignInAsHrAsync();
        var vacation = await DefineKindAsync();
        var sick = await DefineKindAsync(usesAllowance: false);

        var (manager, teamId) = await ATeamAsync(hr);
        var (otherManager, otherTeamId) = await ATeamAsync(hr);

        var ada = await HireAsync(teamId: teamId);
        var ben = await HireAsync(teamId: teamId);
        var cleo = await HireAsync(teamId: teamId);
        var otto = await HireAsync(teamId: otherTeamId);
        foreach (var member in new[] { ada, ben, cleo, otto })
        {
            await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
                new { employeeId = member.Id, absenceKindId = vacation, year = Year, entitled = 26m }));
        }

        await ApprovedAsync(manager, await AskAsync(ada, vacation, Day(0), Day(2)));
        await ApprovedAsync(manager, await AskAsync(ben, sick, Day(1), Day(1)));
        await ApprovedAsync(manager, await AskAsync(cleo, vacation, Day(7), Day(8)));
        await AskAsync(cleo, vacation, Day(3), Day(4));
        await ApprovedAsync(otherManager, await AskAsync(otto, vacation, Day(0), Day(0)));

        return new TeamWithAbsences(manager, ada.Id, ben.Id, cleo.Id, sick);
    }

    /// <summary>A manager and the team they manage; signed in after, since creating it revoked their sessions.</summary>
    private async Task<(HttpClient Manager, Guid TeamId)> ATeamAsync(HttpClient hr)
    {
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        return (await SignInAsync(manager.Account), team.GetProperty("id").GetGuid());
    }

    private async Task<Guid> AskAsync(HiredEmployee employee, Guid kind, string from, string to)
    {
        var client = await SignInAsync(employee.Account);
        var request = await ReadJsonAsync(await client.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to }));
        return request.GetProperty("id").GetGuid();
    }

    private static async Task ApprovedAsync(HttpClient manager, Guid request)
        => await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));

    private sealed record TeamWithAbsences(HttpClient Manager, Guid Ada, Guid Ben, Guid Cleo, Guid SickLeave);
}
