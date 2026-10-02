using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     An employee leaves: HR records it, the employee can no longer sign in and disappears from
///     the searches and the calendar, their history stays, and a termination recorded by mistake is undone.
/// </summary>
public sealed class EmployeesLeaving(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task ATerminatedEmployee_IsSignedOut_AndDisappearsFromSearchesAndTheCalendar()
    {
        var (hr, leaver, session) = await AnEmployeeAwayOnDayOneAsync();

        // The control: before, the same reads find them — so they read the rows, and are not empty by construction.
        (await session.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EmployeesNamedAsync(hr, leaver.Account.FullName)).Should().Contain(leaver.Id);
        (await AwayOnDayOneAsync(hr)).Should().Contain(leaver.Id);

        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{leaver.Id}"));

        (await session.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "terminating closes the sessions already issued");
        (await PostSignInAsync(leaver.Account.WorkEmail, leaver.Account.Password)).IsSuccessStatusCode
            .Should().BeFalse("the account is no longer active");
        (await EmployeesNamedAsync(hr, leaver.Account.FullName)).Should().NotContain(leaver.Id);
        (await AwayOnDayOneAsync(hr)).Should().NotContain(leaver.Id);
    }

    /// <summary>
    ///     A termination answers 204 and nothing else. The deleted row is not an answer: the
    ///     employee carries their account, and serialised it is the password hash and the security stamp.
    /// </summary>
    [Fact]
    public async Task TerminatingAnEmployee_AnswersWithNoBody()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();

        var response = await hr.DeleteAsync($"/api/employees/{employee.Id}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, body);
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoringAnEmployee_BringsThemBack_AndTheirPasswordStillWorks()
    {
        var (hr, leaver, _) = await AnEmployeeAwayOnDayOneAsync();
        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{leaver.Id}"));

        await ReadSuccessAsync(await hr.PostAsJsonAsync($"/api/employees/{leaver.Id}/restore", new { }));

        (await EmployeesNamedAsync(hr, leaver.Account.FullName)).Should().Contain(leaver.Id);
        (await AwayOnDayOneAsync(hr)).Should().Contain(leaver.Id, "their history comes back with them");
        var session = await SignInAsync(leaver.Account);
        (await session.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATeamsManager_CannotBeTerminated_UntilTheTeamHasAnotherOne()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));

        var refused = await hr.DeleteAsync($"/api/employees/{manager.Id}");

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(refused)).Should().Be("EMPLOYEE_MANAGES_A_TEAM");
        (await EmployeesNamedAsync(hr, manager.Account.FullName)).Should().Contain(manager.Id,
            "a refused termination changes nothing");
    }

    [Fact]
    public async Task AnActiveEmployee_CannotBeErased()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();

        var refused = await hr.PostAsJsonAsync($"/api/employees/{employee.Id}/erasure", new { });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(refused)).Should().Be("EMPLOYEE_STILL_ACTIVE");
        (await SignInAsync(employee.Account)).Should().NotBeNull("nothing was erased");
    }

    [Fact]
    public async Task TheAuditTrail_TellsTheTerminationFromTheRestoration()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();

        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{employee.Id}"));
        await ReadSuccessAsync(await hr.PostAsJsonAsync($"/api/employees/{employee.Id}/restore", new { }));

        using var scope = Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = "Employee", Limit = 1000 });

        page.Entries
            .Where(e => e.TargetId == employee.Id.ToString())
            .OrderBy(e => e.OccurredAt)
            .Select(e => e.Operation)
            .Where(o => o is "Data.EntityDeleted" or "Data.EntityRestored")
            .Should().Equal("Data.EntityDeleted", "Data.EntityRestored");
    }

    /// <summary>A member of a team, with an approved day off on <c>Day(1)</c>, and their own session.</summary>
    private async Task<(HttpClient Hr, HiredEmployee Leaver, HttpClient Session)> AnEmployeeAwayOnDayOneAsync()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var leaver = await HireAsync(teamId: team.GetProperty("id").GetGuid());
        var sickLeave = await DefineKindAsync(usesAllowance: false);

        var session = await SignInAsync(leaver.Account);
        var request = (await ReadJsonAsync(await session.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = sickLeave, from = Day(1), to = Day(1) }))).GetProperty("id").GetGuid();
        await ReadJsonAsync(await (await SignInAsync(manager.Account)).PostAsJsonAsync(
            $"/api/leave-requests/{request}/approve", new { }));

        return (hr, leaver, session);
    }

    private static async Task<IReadOnlyList<Guid>> EmployeesNamedAsync(HttpClient hr, string fullName)
    {
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/employees?fullName={Uri.EscapeDataString(fullName)}"));
        return [.. page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];
    }

    private static async Task<IReadOnlyList<Guid>> AwayOnDayOneAsync(HttpClient hr)
    {
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/team-calendar?from={Day(1)}&to={Day(1)}&pageSize=100"));
        return [.. page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("employeeId").GetGuid())];
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
