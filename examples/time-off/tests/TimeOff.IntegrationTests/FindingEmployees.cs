using System.Globalization;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR finds employees by what they know about them: part of a name, an email or a number,
///     who is away today, who has requests waiting for a decision — and any of these together.
/// </summary>
/// <remarks>
///     The database is shared with every other test, so each case reads its own people back: by a team
///     made for it, or by a name nobody else has.
/// </remarks>
public sealed class FindingEmployees(PostgresFixture database) : TimeOffTestBase(database)
{
    /// <summary>The search matches the full name, the work email and the number — whatever their case.</summary>
    [Fact]
    public async Task TheSearch_MatchesEachColumn_WhateverItsCase()
    {
        var hr = await SignInAsHrAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var mailbox = Guid.NewGuid().ToString("N")[..12];
        var created = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/employees",
            new { fullName = $"Ottavia Q{marker}", workEmail = $"{mailbox}@time-off.test", hiredOn = "2024-03-01" }));
        var id = created.GetProperty("id").GetGuid();
        var number = created.GetProperty("employeeNumber").GetString()!;

        (await SearchAsync(hr, $"search=ottavia%20q{marker.ToUpperInvariant()}")).Should().Equal(new[] { id }, "by the full name");
        (await SearchAsync(hr, $"search={mailbox.ToUpperInvariant()}")).Should().Equal(new[] { id }, "by the work email");
        (await SearchAsync(hr, $"search={number.ToLowerInvariant()}")).Should().Equal(new[] { id }, "by the number");
    }

    /// <summary>
    ///     Away today is an approved request that covers today. A pending one does not count, nor an
    ///     approved one next week, nor nothing at all.
    /// </summary>
    [Fact]
    public async Task AwayToday_IsExactlyWhoHasAnApprovedRequestCoveringToday()
    {
        var team = await ATeamAsync();

        Sql.Clear();
        var away = await SearchAsync(team.Hr, $"teamId={team.Id}&awayToday=true");

        away.Should().BeEquivalentTo(team.AwayToday, team.AwayAndWaiting);

        // The page and its count: both read the employees with the rule as a condition in the SQL —
        // nothing loads every employee to ask each one.
        var reads = Sql.Commands.Where(c => c.Contains("\"Employees\"", StringComparison.Ordinal)
                                            && c.Contains("\"LeaveRequests\"", StringComparison.Ordinal)).ToList();
        reads.Should().NotBeEmpty("the search reads the employees and their requests in one statement");
        reads.Should().OnlyContain(c => System.Text.RegularExpressions.Regex.IsMatch(c, "(?i)\\bexists\\b"),
            "the rule is a condition on the requests, computed by the database");
    }

    /// <summary>And the other side of it: everyone in the team who is not away today.</summary>
    [Fact]
    public async Task NotAwayToday_IsEveryoneElse()
    {
        var team = await ATeamAsync();

        var present = await SearchAsync(team.Hr, $"teamId={team.Id}&awayToday=false");

        present.Should().BeEquivalentTo(team.WaitingToday, team.AwayNextWeek, team.Nothing);
    }

    /// <summary>Requests waiting for a decision: pending, whenever they are for.</summary>
    [Fact]
    public async Task HasPendingRequests_IsWhoHasARequestWaitingForADecision()
    {
        var team = await ATeamAsync();

        var waiting = await SearchAsync(team.Hr, $"teamId={team.Id}&hasPendingRequests=true");

        waiting.Should().BeEquivalentTo(team.WaitingToday, team.AwayAndWaiting);
    }

    /// <summary>Two filters together narrow the result: away today, and something still waiting.</summary>
    [Fact]
    public async Task TwoFiltersTogether_NarrowTheResult()
    {
        var team = await ATeamAsync();

        var both = await SearchAsync(team.Hr, $"teamId={team.Id}&awayToday=true&hasPendingRequests=true");

        both.Should().Equal(team.AwayAndWaiting);
    }

    /// <summary>
    ///     A team of its own, and in it: one away today, one with a pending request for today, one away
    ///     next week, one with nothing, and one away today with another request still waiting.
    /// </summary>
    private async Task<Team> ATeamAsync()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var teamId = (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }))).GetProperty("id").GetGuid();
        var approver = await SignInAsync(manager.Account);
        var sickLeave = await DefineKindAsync(usesAllowance: false);

        var awayToday = await HireAsync(teamId: teamId);
        var waitingToday = await HireAsync(teamId: teamId);
        var awayNextWeek = await HireAsync(teamId: teamId);
        var nothing = await HireAsync(teamId: teamId);
        var awayAndWaiting = await HireAsync(teamId: teamId);

        var (weekStart, weekEnd) = AroundToday();
        var (nextStart, nextEnd) = NextWeek();

        await ApproveAsync(approver, await AskAsync(awayToday, sickLeave, weekStart, weekEnd));
        await AskAsync(waitingToday, sickLeave, weekStart, weekEnd);
        await ApproveAsync(approver, await AskAsync(awayNextWeek, sickLeave, nextStart, nextEnd));
        await ApproveAsync(approver, await AskAsync(awayAndWaiting, sickLeave, weekStart, weekEnd));
        await AskAsync(awayAndWaiting, sickLeave, Day(0), Day(0));

        return new Team(hr, teamId, awayToday.Id, waitingToday.Id, awayNextWeek.Id, nothing.Id, awayAndWaiting.Id);
    }

    /// <summary>
    ///     Today and the days around it, inside this year — a request cannot span two — and wide enough
    ///     to hold a working day whatever today is.
    /// </summary>
    private static (string From, string To) AroundToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-3);
        var to = today.AddDays(3);
        if (from.Year != today.Year) from = new DateOnly(today.Year, 1, 1);
        if (to.Year != today.Year) to = new DateOnly(today.Year, 12, 31);
        return (Iso(from), Iso(to));
    }

    /// <summary>
    ///     A week from now, for a request that does not cover today — or next February, when a week from
    ///     now would cross into next year.
    /// </summary>
    private static (string From, string To) NextWeek()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(7);
        var to = today.AddDays(11);
        return to.Year == today.Year ? (Iso(from), Iso(to)) : (Day(7), Day(11));
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Guid> AskAsync(HiredEmployee employee, Guid kind, string from, string to)
    {
        var session = await SignInAsync(employee.Account);
        var request = await ReadJsonAsync(await session.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to }));
        return request.GetProperty("id").GetGuid();
    }

    private static async Task ApproveAsync(HttpClient manager, Guid request)
        => await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));

    private static async Task<IReadOnlyList<Guid>> SearchAsync(HttpClient hr, string query)
    {
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/employees?{query}&pageSize=100"));
        return [.. page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];
    }

    /// <summary>The manager heads the team and is not in it: <c>HireAsync</c> gives a manager no team.</summary>
    private sealed record Team(
        HttpClient Hr, Guid Id,
        Guid AwayToday, Guid WaitingToday, Guid AwayNextWeek, Guid Nothing, Guid AwayAndWaiting);
}
