using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     At year end HR carries over what each employee has left of a kind into the next year,
///     up to a cap: the free amount, as a submission counts it, and never added twice.
/// </summary>
/// <remarks>
///     Each test defines a kind of its own: the carry-over reaches every allowance of the kind in the
///     year, and the database is shared.
/// </remarks>
public sealed class CarryingOverTheYear(PostgresFixture database) : TimeOffTestBase(database)
{
    private static int NextYear => Year + 1;

    [Fact]
    public async Task FiveFreeWithACapOfThree_CarriesThree_AndTwoFreeCarriesTwo()
    {
        var kind = await DefineKindAsync();
        var five = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);
        var two = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 2m);

        var carried = await CarryOverAsync(kind, cap: 3m);

        CarriedTo(carried, five.Employee).Should().Be(3m);
        CarriedTo(carried, two.Employee).Should().Be(2m);
        carried.EnumerateArray().Single(c => c.GetProperty("employeeId").GetGuid() == five.Employee.Id)
            .GetProperty("employeeNumber").GetString().Should().StartWith("EMP-");
    }

    /// <summary>The next year's balance, as the employee reads it, holds what was carried.</summary>
    [Fact]
    public async Task TheNextYearsBalance_ShowsTheCarriedDays()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);

        await CarryOverAsync(kind, cap: 3m);

        var next = await BalanceAsync(member.Session, kind, NextYear);
        next.GetProperty("entitled").GetDecimal().Should().Be(0m, "the next year's allowance did not exist: it is created with nothing granted");
        next.GetProperty("carriedOver").GetDecimal().Should().Be(3m);
        next.GetProperty("remaining").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task NothingFree_CarriesZero()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 2m);
        var request = await AskAsync(member.Session, kind, Day(0), Day(1));
        await ReadJsonAsync(await member.Manager.PostAsJsonAsync($"/api/leave-requests/{request}/approve", new { }));

        var carried = await CarryOverAsync(kind, cap: 3m);

        CarriedTo(carried, member.Employee).Should().Be(0m);
    }

    /// <summary>Pending days count as taken, as they do when a request is submitted: 5 less 2 pending is 3.</summary>
    [Fact]
    public async Task PendingDays_AreNotCarried()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);
        await AskAsync(member.Session, kind, Day(0), Day(1));

        var carried = await CarryOverAsync(kind, cap: 10m);

        CarriedTo(carried, member.Employee).Should().Be(3m);
        (await BalanceAsync(member.Session, kind, NextYear)).GetProperty("carriedOver").GetDecimal().Should().Be(3m);
    }

    /// <summary>It sets, it never adds: the second run answers and leaves exactly what the first did.</summary>
    [Fact]
    public async Task ASecondRun_LeavesTheNumbersUnchanged()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);

        var first = await CarryOverAsync(kind, cap: 3m);
        var second = await CarryOverAsync(kind, cap: 3m);

        CarriedTo(second, member.Employee).Should().Be(CarriedTo(first, member.Employee));
        var next = await BalanceAsync(member.Session, kind, NextYear);
        next.GetProperty("carriedOver").GetDecimal().Should().Be(3m);
        next.GetProperty("entitled").GetDecimal().Should().Be(0m);
        (await AllowancesAsync(member.Session, kind, NextYear)).Should().Be(1, "the second run did not create another one");
    }

    [Fact]
    public async Task AnExistingNextYearAllowance_KeepsItsEntitlement()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);
        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = member.Employee.Id, absenceKindId = kind, year = NextYear, entitled = 26m }));

        await CarryOverAsync(kind, cap: 3m);

        var next = await BalanceAsync(member.Session, kind, NextYear);
        next.GetProperty("entitled").GetDecimal().Should().Be(26m);
        next.GetProperty("carriedOver").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task AKindWithoutAnAllowance_IsRefused()
    {
        var kind = await DefineKindAsync(usesAllowance: false);
        var hr = await SignInAsHrAsync();

        var response = await hr.PostAsJsonAsync("/api/allowances/carry-over",
            new { absenceKindId = kind, fromYear = Year, cap = 3m });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        body.Should().Contain("absenceKindId", "the error names the field the rule is about");
    }

    [Fact]
    public async Task AnEmployee_IsRefused()
    {
        var kind = await DefineKindAsync();
        var member = await AnEmployeeWithAnAllowanceAsync(kind, entitled: 5m);

        var response = await member.Session.PostAsJsonAsync("/api/allowances/carry-over",
            new { absenceKindId = kind, fromYear = Year, cap = 3m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AllowancesAsync(member.Session, kind, NextYear)).Should().Be(0);
    }

    private async Task<JsonElement> CarryOverAsync(Guid kind, decimal cap)
    {
        var hr = await SignInAsHrAsync();
        return await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances/carry-over",
            new { absenceKindId = kind, fromYear = Year, cap }));
    }

    private static decimal CarriedTo(JsonElement carried, HiredEmployee employee)
        => carried.EnumerateArray().Single(c => c.GetProperty("employeeId").GetGuid() == employee.Id)
            .GetProperty("carriedOver").GetDecimal();

    private static async Task<JsonElement> BalanceAsync(HttpClient employee, Guid kind, int year)
    {
        var balances = await ReadJsonAsync(await employee.GetAsync($"/api/me/balances?year={year}"));
        return balances.EnumerateArray().Single(b => b.GetProperty("absenceKindId").GetGuid() == kind);
    }

    private static async Task<int> AllowancesAsync(HttpClient employee, Guid kind, int year)
    {
        var balances = await ReadJsonAsync(await employee.GetAsync($"/api/me/balances?year={year}"));
        return balances.EnumerateArray().Count(b => b.GetProperty("absenceKindId").GetGuid() == kind);
    }

    private static async Task<Guid> AskAsync(HttpClient employee, Guid kind, string from, string to)
        => (await ReadJsonAsync(await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = kind, from, to }))).GetProperty("id").GetGuid();

    /// <summary>A member of a team, with an allowance of <paramref name="kind" /> this test year, their session and their manager's.</summary>
    private async Task<Member> AnEmployeeWithAnAllowanceAsync(Guid kind, decimal entitled)
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var employee = await HireAsync(teamId: team.GetProperty("id").GetGuid());
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = Year, entitled }));

        return new Member(employee, await SignInAsync(employee.Account), await SignInAsync(manager.Account));
    }

    private sealed record Member(HiredEmployee Employee, HttpClient Session, HttpClient Manager);
}
