using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     A leave request's detail in one answer: who asked, the kind named in the reader's
///     language, the period and the amount, and the decision — read in a single SQL statement.
/// </summary>
public sealed class ReadingALeaveRequest(PostgresFixture database) : TimeOffTestBase(database)
{
    /// <summary>A decided request answers with every nested object filled in.</summary>
    [Fact]
    public async Task ADecidedRequest_AnswersWithEveryNestedObject()
    {
        var team = await ATeamAsync();
        var request = await AskAsync(team.Member, team.Kind, Day(0), Day(1));
        await ReadJsonAsync(await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { note = "Enjoy" }));

        var detail = await ReadAsync(team.Hr, request, "en-US");

        detail.GetProperty("from").GetString().Should().Be(Day(0));
        detail.GetProperty("to").GetString().Should().Be(Day(1));
        detail.GetProperty("amount").GetDecimal().Should().Be(2m);
        detail.GetProperty("status").GetString().Should().Be("Approved");

        var employee = detail.GetProperty("employee");
        employee.GetProperty("id").GetGuid().Should().Be(team.MemberId);
        employee.GetProperty("employeeNumber").GetString().Should().MatchRegex(@"^EMP-\d{5}$");
        employee.GetProperty("fullName").GetString().Should().Be(team.MemberName);

        var kind = detail.GetProperty("absenceKind");
        kind.GetProperty("id").GetGuid().Should().Be(team.Kind);
        kind.GetProperty("name").GetString().Should().Be("A kind");

        var decision = detail.GetProperty("decision");
        decision.GetProperty("decidedBy").GetProperty("id").GetGuid().Should().Be(team.ManagerId);
        decision.GetProperty("decidedBy").GetProperty("fullName").GetString().Should().Be(team.ManagerName);
        decision.GetProperty("decidedBy").GetProperty("employeeNumber").GetString().Should().MatchRegex(@"^EMP-\d{5}$");
        decision.GetProperty("decidedAt").ValueKind.Should().Be(JsonValueKind.String);
        decision.GetProperty("note").GetString().Should().Be("Enjoy");
    }

    /// <summary>A pending request has no decision yet: <c>decision</c> is null, not an empty object.</summary>
    [Fact]
    public async Task APendingRequest_HasNoDecision()
    {
        var team = await ATeamAsync();
        var request = await AskAsync(team.Member, team.Kind, Day(0), Day(0));

        var detail = await ReadAsync(team.Hr, request, "en-US");

        detail.GetProperty("status").GetString().Should().Be("Pending");
        (!detail.TryGetProperty("decision", out var decision) || decision.ValueKind == JsonValueKind.Null)
            .Should().BeTrue("a pending request is decided by nobody yet: " + detail);
        detail.GetProperty("employee").GetProperty("id").GetGuid().Should().Be(team.MemberId,
            "the control: the rest of the detail is there");
    }

    /// <summary>The kind is named in the language of the request.</summary>
    [Fact]
    public async Task TheKind_IsNamedInTheLanguageOfTheRequest()
    {
        var team = await ATeamAsync();
        var request = await AskAsync(team.Member, team.Kind, Day(0), Day(0));

        (await ReadAsync(team.Hr, request, "it-IT")).GetProperty("absenceKind").GetProperty("name").GetString()
            .Should().Be("Un tipo");
        (await ReadAsync(team.Hr, request, "en-US")).GetProperty("absenceKind").GetProperty("name").GetString()
            .Should().Be("A kind", "the control: the same request, another language");
    }

    /// <summary>The whole answer is one SQL statement: the request, its employee, its kind and its decider.</summary>
    [Fact]
    public async Task TheWholeAnswer_IsOneStatement()
    {
        var team = await ATeamAsync();
        var request = await AskAsync(team.Member, team.Kind, Day(0), Day(0));
        await ReadJsonAsync(await team.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));

        Sql.Clear();
        await ReadAsync(team.Hr, request, "en-US");

        var reads = Sql.Commands.Where(c => c.Contains("\"LeaveRequests\"", StringComparison.Ordinal)).ToList();
        reads.Should().ContainSingle("the detail is read once");
        reads[0].Should().Contain("\"Employees\"").And.Contain("\"AbsenceKinds\"");
    }

    /// <summary>Visibility is unchanged: another team's manager does not find the request.</summary>
    [Fact]
    public async Task AnotherTeamsManager_DoesNotFindTheRequest()
    {
        var team = await ATeamAsync();
        var other = await ATeamAsync();
        var request = await AskAsync(team.Member, team.Kind, Day(0), Day(0));

        (await other.Manager.GetAsync($"/api/leave-requests/{request}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await team.Manager.GetAsync($"/api/leave-requests/{request}")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the control: their own manager does");
    }

    private async Task<Team> ATeamAsync()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var teamId = (await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }))).GetProperty("id").GetGuid();
        var member = await HireAsync(teamId: teamId);
        var kind = await DefineKindAsync(usesAllowance: false);

        return new Team(hr, await SignInAsync(manager.Account), manager.Id, manager.Account.FullName,
            await SignInAsync(member.Account), member.Id, member.Account.FullName, kind);
    }

    private static async Task<Guid> AskAsync(HttpClient employee, Guid kind, string from, string to)
        => (await ReadJsonAsync(await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to }))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid request, string language)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"/api/leave-requests/{request}");
        message.Headers.AcceptLanguage.ParseAdd(language);
        return await ReadJsonAsync(await client.SendAsync(message));
    }

    private sealed record Team(
        HttpClient Hr, HttpClient Manager, Guid ManagerId, string ManagerName,
        HttpClient Member, Guid MemberId, string MemberName, Guid Kind);
}
