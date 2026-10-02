using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR moves an employee to another team: the requests still waiting for a decision go with
///     them to the new manager, and what was already decided stays where it was decided.
/// </summary>
public sealed class TransferringAnEmployee(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task APendingRequest_FollowsTheEmployee_ToTheNewManager()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();
        var pending = await AskAsync(move, Day(0));

        await TransferAsync(move, move.NewTeam);

        (await move.OldManager.GetAsync($"/api/leave-requests/{pending}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the old manager no longer decides it");
        (await move.NewManager.GetAsync($"/api/leave-requests/{pending}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var approved = await ReadJsonAsync(await move.NewManager.PostAsJsonAsync(
            $"/api/leave-requests/{pending}/approve", new { }));
        approved.GetProperty("status").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task AnApprovedRequest_StaysWhereItWasDecided()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();
        var decided = await AskAsync(move, Day(0));
        await ReadJsonAsync(await move.OldManager.PostAsJsonAsync($"/api/leave-requests/{decided}/approve", new { }));

        await TransferAsync(move, move.NewTeam);

        (await move.OldManager.GetAsync($"/api/leave-requests/{decided}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "it was decided in the old team, and stays there");
        (await move.NewManager.GetAsync($"/api/leave-requests/{decided}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheEmployee_ReadsBackWithTheNewTeam()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();

        var answer = await TransferAsync(move, move.NewTeam);

        answer.GetProperty("previousTeamId").GetGuid().Should().Be(move.OldTeam);
        var employee = await ReadJsonAsync(await move.Hr.GetAsync($"/api/employees/{move.Employee.Id}"));
        employee.GetProperty("teamId").GetGuid().Should().Be(move.NewTeam);
    }

    /// <summary>
    ///     Each moved request is a change of the request, recorded as one — by the transfer — and a request
    ///     that did not move has no such entry.
    /// </summary>
    [Fact]
    public async Task TheAuditTrail_HasAnUpdateForEachMovedRequest()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();
        var first = await AskAsync(move, Day(0));
        var second = await AskAsync(move, Day(1));
        var decided = await AskAsync(move, Day(2));
        await ReadJsonAsync(await move.OldManager.PostAsJsonAsync($"/api/leave-requests/{decided}/approve", new { }));

        // The database is shared: only what was recorded from the transfer on is read.
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var answer = await TransferAsync(move, move.NewTeam);

        answer.GetProperty("movedRequests").EnumerateArray().Select(r => r.GetGuid())
            .Should().BeEquivalentTo([first, second]);

        using var scope = Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = "LeaveRequest", From = since, Limit = 1000 });
        var byTheTransfer = page.Entries
            .Where(e => e.Operation == "Data.EntityUpdated"
                        && e.BusinessOperation?.EndsWith("TransferEmployeeAction", StringComparison.Ordinal) == true)
            .Select(e => e.TargetId)
            .ToList();

        byTheTransfer.Should().Contain(first.ToString()).And.Contain(second.ToString());
        byTheTransfer.Should().NotContain(decided.ToString(), "an approved request did not move");
    }

    /// <summary>Moving to the team they are already in is an answer, not an error: nothing moves.</summary>
    [Fact]
    public async Task MovingToTheTeamTheyAreIn_ChangesNothing()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();
        var pending = await AskAsync(move, Day(0));

        var answer = await TransferAsync(move, move.OldTeam);

        answer.GetProperty("movedRequests").GetArrayLength().Should().Be(0);
        (await move.OldManager.GetAsync($"/api/leave-requests/{pending}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await move.Session.GetAsync("/api/me")).StatusCode
            .Should().Be(HttpStatusCode.OK, "nothing changed, so the session goes on");
    }

    /// <summary>
    ///     Nothing in a member's token depends on their team (the token carries their access
    ///     role and the teams they manage), so a transfer has nothing to revoke: the session goes on.
    /// </summary>
    [Fact]
    public async Task TheEmployeesSessions_GoOnAfterTheTransfer()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();

        await TransferAsync(move, move.NewTeam);

        (await move.Session.GetAsync("/api/me")).StatusCode
            .Should().Be(HttpStatusCode.OK, "a member's token names no team, so the move changes nothing it says");
    }

    [Fact]
    public async Task ATeamThatDoesNotExist_IsNotFound()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();

        var response = await move.Hr.PostAsJsonAsync($"/api/employees/{move.Employee.Id}/transfer",
            new { teamId = Guid.NewGuid() });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("\"NOT_FOUND\"");
    }

    [Fact]
    public async Task AnEmployee_IsRefused()
    {
        var move = await AnEmployeeBetweenTwoTeamsAsync();

        var response = await move.Session.PostAsJsonAsync($"/api/employees/{move.Employee.Id}/transfer",
            new { teamId = move.NewTeam });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var employee = await ReadJsonAsync(await move.Hr.GetAsync($"/api/employees/{move.Employee.Id}"));
        employee.GetProperty("teamId").GetGuid().Should().Be(move.OldTeam);
    }

    private static async Task<System.Text.Json.JsonElement> TransferAsync(Move move, Guid teamId)
        => await ReadJsonAsync(await move.Hr.PostAsJsonAsync($"/api/employees/{move.Employee.Id}/transfer",
            new { teamId }));

    private static async Task<Guid> AskAsync(Move move, string day)
        => (await ReadJsonAsync(await move.Session.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = move.Kind, from = day, to = day }))).GetProperty("id").GetGuid();

    /// <summary>
    ///     Two teams, each with its own manager, and an employee in the first; the managers sign in after
    ///     their teams exist, so their tokens carry them.
    /// </summary>
    private async Task<Move> AnEmployeeBetweenTwoTeamsAsync()
    {
        var hr = await SignInAsHrAsync();
        var oldManager = await HireAsync(role: "Manager");
        var newManager = await HireAsync(role: "Manager");
        var oldTeam = await TeamOfAsync(hr, oldManager);
        var newTeam = await TeamOfAsync(hr, newManager);
        var employee = await HireAsync(teamId: oldTeam);
        var kind = await DefineKindAsync(usesAllowance: false);

        return new Move(hr, await SignInAsync(oldManager.Account), await SignInAsync(newManager.Account),
            employee, await SignInAsync(employee.Account), oldTeam, newTeam, kind);
    }

    private static async Task<Guid> TeamOfAsync(HttpClient hr, HiredEmployee manager)
        => (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }))).GetProperty("id").GetGuid();

    private sealed record Move(
        HttpClient Hr, HttpClient OldManager, HttpClient NewManager,
        HiredEmployee Employee, HttpClient Session, Guid OldTeam, Guid NewTeam, Guid Kind);
}
