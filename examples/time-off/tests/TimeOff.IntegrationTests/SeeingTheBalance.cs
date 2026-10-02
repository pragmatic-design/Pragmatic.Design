using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     An employee sees what is left of each kind this year: the allowance less what is
///     approved, with what is pending shown apart. The database computes it.
/// </summary>
public sealed class SeeingTheBalance(PostgresFixture database) : TimeOffTestBase(database)
{
    /// <summary>
    ///     22 days (20 granted, 2 carried over); 3 approved, 2 pending, 1 rejected and 1 withdrawn:
    ///     19 left, and 2 of those already asked for.
    /// </summary>
    [Fact]
    public async Task TheBalance_IsTheAllowanceLessWhatIsApproved_WithWhatIsPendingApart()
    {
        var (manager, member, vacation) = await AnEmployeeWithAnAllowanceAsync(entitled: 20m, carriedOver: 2m);

        var approved = await AskAsync(member, vacation, Day(0), Day(2));
        await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{approved}/approve", new { }));
        await AskAsync(member, vacation, Day(3), Day(4));
        var rejected = await AskAsync(member, vacation, Day(7), Day(7));
        await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{rejected}/reject", new { }));
        var withdrawn = await AskAsync(member, vacation, Day(8), Day(8));
        await ReadJsonAsync(await member.PostAsJsonAsync($"/api/leave-requests/{withdrawn}/withdraw", new { }));

        var balance = await BalanceOfAsync(member, vacation);

        balance.GetProperty("entitled").GetDecimal().Should().Be(20m);
        balance.GetProperty("carriedOver").GetDecimal().Should().Be(2m);
        balance.GetProperty("approved").GetDecimal().Should().Be(3m);
        balance.GetProperty("pending").GetDecimal().Should().Be(2m);
        balance.GetProperty("remaining").GetDecimal().Should().Be(19m);
    }

    /// <summary>An allowance nothing was asked of yet is all left.</summary>
    [Fact]
    public async Task AnUntouchedAllowance_IsAllLeft()
    {
        var (_, member, vacation) = await AnEmployeeWithAnAllowanceAsync(entitled: 26m, carriedOver: 0m);

        var balance = await BalanceOfAsync(member, vacation);

        balance.GetProperty("approved").GetDecimal().Should().Be(0m);
        balance.GetProperty("pending").GetDecimal().Should().Be(0m);
        balance.GetProperty("remaining").GetDecimal().Should().Be(26m);
    }

    /// <summary>
    ///     One query reads the balance, and the sums are in it: the requests are added up by the
    ///     database, not loaded and added up by the application.
    /// </summary>
    [Fact]
    public async Task TheBalance_IsComputedByTheDatabase()
    {
        var (manager, member, vacation) = await AnEmployeeWithAnAllowanceAsync(entitled: 20m, carriedOver: 0m);
        var approved = await AskAsync(member, vacation, Day(0), Day(2));
        await ReadJsonAsync(await manager.PostAsJsonAsync($"/api/leave-requests/{approved}/approve", new { }));

        Sql.Clear();
        await BalanceOfAsync(member, vacation);

        var reads = Sql.Commands.Where(c => c.Contains("\"Allowances\"", StringComparison.Ordinal)).ToList();
        reads.Should().ContainSingle();
        reads[0].Should().Contain("\"LeaveRequests\"").And.MatchRegex("(?i)\\bsum\\(");
        Sql.Commands.Where(c => c.Contains("\"LeaveRequests\"", StringComparison.Ordinal)).Should()
            .Equal(reads, "no query reads the requests by themselves");
    }

    /// <summary>
    ///     Whose balances they are is the caller. The query binds the employee from the
    ///     signed-in user, so it is not a parameter: a query string naming someone else is not read.
    /// </summary>
    [Fact]
    public async Task SomeoneElsesId_InTheQueryString_ChangesNothing()
    {
        var (_, one, oneKind) = await AnEmployeeWithAnAllowanceAsync(entitled: 20m, carriedOver: 0m);
        var (_, other, otherKind) = await AnEmployeeWithAnAllowanceAsync(entitled: 26m, carriedOver: 0m);
        var otherId = (await ReadJsonAsync(await other.GetAsync("/api/me"))).GetProperty("id").GetGuid();

        var balances = await ReadJsonAsync(
            await one.GetAsync($"/api/me/balances?year={Year}&employeeId={otherId}"));

        var kinds = balances.EnumerateArray().Select(b => b.GetProperty("absenceKindId").GetGuid()).ToList();
        kinds.Should().Contain(oneKind);
        kinds.Should().NotContain(otherKind);
    }

    private static async Task<JsonElement> BalanceOfAsync(HttpClient employee, Guid kind)
    {
        var balances = await ReadJsonAsync(await employee.GetAsync($"/api/me/balances?year={Year}"));
        return balances.EnumerateArray().Single(b => b.GetProperty("absenceKindId").GetGuid() == kind);
    }

    private static async Task<Guid> AskAsync(HttpClient employee, Guid kind, string from, string to)
    {
        var request = await ReadJsonAsync(await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to }));
        return request.GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Manager, HttpClient Member, Guid Vacation)> AnEmployeeWithAnAllowanceAsync(
        decimal entitled, decimal carriedOver)
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var member = await HireAsync(teamId: team.GetProperty("id").GetGuid());

        var vacation = await DefineKindAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances", new
        {
            employeeId = member.Id, absenceKindId = vacation, year = Year, entitled, carriedOver
        }));

        return (await SignInAsync(manager.Account), await SignInAsync(member.Account), vacation);
    }

}
