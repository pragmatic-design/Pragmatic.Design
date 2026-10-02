using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     A manager decides the requests of their team and only theirs; an employee withdraws a
///     request of theirs before it starts; each decision is recorded once.
/// </summary>
/// <remarks>
///     Requests fall in February of next year (<see cref="TestCalendar" />). The clock is pinned to the
///     second the class starts, so a decision can be checked against the instant the application read,
///     and everything else sees the day it is.
/// </remarks>
public sealed class DecidingLeaveRequests(PostgresFixture database) : TimeOffTestBase(database)
{
    private static readonly DateTimeOffset Pinned =
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(new TestClock(Pinned));

    [Fact]
    public async Task AManager_ApprovesARequestOfTheirTeam()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));

        var approved = await ReadJsonAsync(await team.Manager.PostAsJsonAsync(
            $"/api/leave-requests/{request}/approve", new { note = "Enjoy" }));

        approved.GetProperty("status").GetString().Should().Be("Approved");
        approved.GetProperty("decisionNote").GetString().Should().Be("Enjoy");
        approved.GetProperty("decidedAt").GetDateTimeOffset().Should().Be(Pinned,
            "the decision is stamped with the application's clock, which the invoker writes into the mutation");
    }

    [Fact]
    public async Task AManager_RejectsARequestOfTheirTeam_AtTheClocksInstant()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));

        var rejected = await ReadJsonAsync(await team.Manager.PostAsJsonAsync(
            $"/api/leave-requests/{request}/reject", new { note = "Busy week" }));

        rejected.GetProperty("status").GetString().Should().Be("Rejected");
        rejected.GetProperty("decidedAt").GetDateTimeOffset().Should().Be(Pinned);
    }

    /// <summary>Not a 403: whether the request exists is not the other manager's to know.</summary>
    [Fact]
    public async Task AnotherManager_DoesNotFindTheRequest()
    {
        var team = await TeamWithAMemberAsync();
        var other = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));

        var response = await other.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await other.Manager.GetAsync($"/api/leave-requests/{request}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApprovingARejectedRequest_IsAConflictFromTheStateMachine()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));
        await ReadJsonAsync(await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/reject", new { note = "Busy week" }));

        var response = await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Cannot transition from 'Rejected' to 'Approved'");
    }

    [Fact]
    public async Task AManager_CannotDecideTheirOwnRequest()
    {
        var team = await TeamWithAMemberAsync();
        var own = await ReadJsonAsync(await team.Manager.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = team.SickLeave, from = Day(14), to = Day(15) }));

        var response = await team.Manager.PostAsJsonAsync(
            $"/api/leave-requests/{own.GetProperty("id").GetGuid()}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CANNOT_DECIDE_OWN_REQUEST");
    }

    /// <summary>
    ///     A colleague, not the requester: the requester would be refused as deciding their own request,
    ///     and the test would pass without the permission.
    /// </summary>
    [Fact]
    public async Task AnEmployee_CannotDecide()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));
        var colleague = await SignInAsync((await HireAsync(teamId: team.Id)).Account);

        var response = await colleague.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("requiredPermissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal(LeavePermissions.LeaveRequest.Decide);
        problem.GetProperty("permissionMatch").GetString().Should().Be("all");
    }

    /// <summary>
    ///     The refusal of the HTTP policy goes through the path every other error takes, so it
    ///     is in the language asked, with the missing permission in its place.
    /// </summary>
    /// <remarks>
    ///     ⚠️ What makes this assertion mean anything is
    ///     <see cref="TheSuiteDoesNotInheritTheMachinesLocale" />: the process' default culture is
    ///     <c>en-US</c>, so the only thing that can answer in Italian is the <c>Accept-Language</c>
    ///     header. With the machine's locale answering, this test would be green on an Italian
    ///     machine and red on the runner.
    /// </remarks>
    [Fact]
    public async Task AnEmployee_IsRefusedInTheLanguageAsked()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));
        var colleague = await SignInAsync((await HireAsync(teamId: team.Id)).Account);

        using var approve = new HttpRequestMessage(HttpMethod.Post, $"/api/leave-requests/{request}/approve")
        {
            Content = JsonContent.Create(new { })
        };
        approve.Headers.AcceptLanguage.ParseAdd("it-IT");
        var response = await colleague.SendAsync(approve);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().Should().Be("FORBIDDEN");
        problem.GetProperty("title").GetString().Should().Be("Non consentito");
        problem.GetProperty("detail").GetString().Should().Be($"Ti manca il permesso che serve: {LeavePermissions.LeaveRequest.Decide}.");
    }

    [Fact]
    public async Task TheManager_SeesTheTeamsPendingRequests_AndNoOtherTeamsOnes()
    {
        var team = await TeamWithAMemberAsync();
        var other = await TeamWithAMemberAsync();
        var mine = await team.AskAsync(Day(0), Day(2));
        var theirs = await other.AskAsync(Day(0), Day(2));

        var page = await ReadJsonAsync(await team.Manager.GetAsync("/api/leave-requests?status=Pending"));
        var ids = page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(mine);
        ids.Should().NotContain(theirs);
    }

    /// <summary>What a withdrawn request took from the allowance is free again.</summary>
    [Fact]
    public async Task AnEmployee_WithdrawsBeforeTheStart_AndTheDaysAreFreeAgain()
    {
        var team = await TeamWithAMemberAsync(allowanceDays: 3m);
        var request = await team.AskAsync(Day(0), Day(2));

        var withdrawn = await ReadJsonAsync(await team.Member.PostAsJsonAsync($"/api/leave-requests/{request}/withdraw", new { }));

        withdrawn.GetProperty("status").GetString().Should().Be("Withdrawn");
        (await team.AskAsync(Day(7), Day(9))).Should().NotBeEmpty("the three days are back");
    }

    /// <summary>
    ///     January of this year has always started, and always holds working days. Sick leave, which
    ///     needs no allowance: it is often entered after the fact.
    /// </summary>
    [Fact]
    public async Task AnEmployee_CannotWithdrawOnceItStarted()
    {
        var team = await TeamWithAMemberAsync();
        var year = DateTime.UtcNow.Year;
        var started = await ReadJsonAsync(await team.Member.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = team.SickLeave, from = $"{year}-01-01", to = $"{year}-01-31" }));

        var response = await team.Member.PostAsJsonAsync(
            $"/api/leave-requests/{started.GetProperty("id").GetGuid()}/withdraw", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LEAVE_ALREADY_STARTED");
    }

    /// <summary>The manager sees the request and decides it, and still cannot take it back for the employee.</summary>
    [Fact]
    public async Task TheManager_CannotWithdrawForTheEmployee()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));

        var response = await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/withdraw", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NOT_YOUR_REQUEST");
    }

    [Fact]
    public async Task EachDecision_IsRecordedOnce_WithWhoTookIt()
    {
        var team = await TeamWithAMemberAsync();
        var request = await team.AskAsync(Day(0), Day(2));
        await ReadJsonAsync(await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));
        await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { });

        var hr = await SignInAsHrAsync();
        var decisions = await ReadJsonAsync(await hr.GetAsync($"/api/leave-requests/{request}/decisions"));

        decisions.GetArrayLength().Should().Be(1, "the second approval was refused, and a refusal decides nothing");
        decisions[0].GetProperty("operation").GetString().Should().Be("Leave.RequestApproved");
        decisions[0].GetProperty("decidedBy").GetString().Should().Be(team.ManagerId.ToString("N"));
    }

    private async Task<TeamUnderTest> TeamWithAMemberAsync(decimal allowanceDays = 26m)
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var teamId = team.GetProperty("id").GetGuid();
        var member = await HireAsync(teamId: teamId);

        var vacation = await DefineKindAsync();
        var sick = await DefineKindAsync(usesAllowance: false);
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = member.Id, absenceKindId = vacation, year = Year, entitled = allowanceDays }));

        // Signed in after the team exists: creating it revoked the manager's earlier sessions.
        return new TeamUnderTest(teamId, await SignInAsync(manager.Account), manager.Id,
            await SignInAsync(member.Account), vacation, sick);
    }

    private sealed record TeamUnderTest(Guid Id, HttpClient Manager, Guid ManagerId, HttpClient Member, Guid Vacation, Guid SickLeave)
    {
        public async Task<Guid> AskAsync(string from, string to)
        {
            var request = await ReadJsonAsync(await Member.PostAsJsonAsync("/api/leave-requests",
                new { absenceKindId = Vacation, from, to }));
            return request.GetProperty("id").GetGuid();
        }
    }
}
