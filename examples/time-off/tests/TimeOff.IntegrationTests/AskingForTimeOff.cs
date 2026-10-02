using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     An employee asks for time off: the working days are counted, an overlap and an
///     overdraft are refused, each with its own code.
/// </summary>
/// <remarks>
///     Dates are 2026 and chosen for what surrounds them: 1 May is a Friday, 2 June a Tuesday, Easter
///     Monday is 6 April. Every test hires its own employee, so requests never meet across tests.
/// </remarks>
public sealed class AskingForTimeOff(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task APeriodAcrossAWeekendAndAPublicHoliday_CountsOnlyTheWorkingDays()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        // Thu 30 Apr, [Fri 1 May: Festa del Lavoro], [Sat 2, Sun 3], Mon 4, Tue 5.
        var request = await ReadJsonAsync(await Ask(employee, vacation, "2026-04-30", "2026-05-05"));

        request.GetProperty("amount").GetDecimal().Should().Be(3m);
        request.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task EasterMonday_IsNotAWorkingDay()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        // [Mon 6 Apr: Easter Monday], Tue 7, Wed 8.
        var request = await ReadJsonAsync(await Ask(employee, vacation, "2026-04-06", "2026-04-08"));

        request.GetProperty("amount").GetDecimal().Should().Be(2m);
    }

    [Fact]
    public async Task ACompanyClosure_IsNotCountedEither()
    {
        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/company-holidays", new
        {
            date = "2026-06-01",
            name = new Dictionary<string, string> { ["en-US"] = "Bridge day", ["it-IT"] = "Ponte" },
            kind = "Closure"
        }));
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        // [Mon 1 Jun: closure], [Tue 2 Jun: Festa della Repubblica], Wed 3, Thu 4, Fri 5.
        var request = await ReadJsonAsync(await Ask(employee, vacation, "2026-06-01", "2026-06-05"));

        request.GetProperty("amount").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task AnOverlappingRequest_IsRefused_WithItsCode()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);
        await ReadJsonAsync(await Ask(employee, vacation, "2026-07-06", "2026-07-10"));

        var second = await Ask(employee, vacation, "2026-07-09", "2026-07-14");

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(second)).Should().Be("LEAVE_REQUEST_OVERLAPS");
    }

    [Fact]
    public async Task ARequestBeyondTheAllowance_IsRefused_WithADifferentCode()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 5m);

        // Mon 7 – Fri 18 Sep: ten working days, against five.
        var response = await Ask(employee, vacation, "2026-09-07", "2026-09-18");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("ALLOWANCE_EXCEEDED");
    }

    /// <summary>What is pending is taken until it is decided otherwise.</summary>
    [Fact]
    public async Task APendingRequest_CountsAgainstTheAllowance()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 5m);
        await ReadJsonAsync(await Ask(employee, vacation, "2026-09-07", "2026-09-09"));

        var second = await Ask(employee, vacation, "2026-09-14", "2026-09-16");

        (await CodeOf(second)).Should().Be("ALLOWANCE_EXCEEDED", "three pending and three more is six, against five");
    }

    [Fact]
    public async Task AnEndBeforeTheStart_Is422_NamingTheField()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        var response = await Ask(employee, vacation, "2026-10-12", "2026-10-09");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        body.Should().ContainEquivalentOf("\"to\"", "the error names the field that is wrong");
    }

    /// <summary>The allowance is yearly, so a request stays in one year.</summary>
    [Fact]
    public async Task APeriodAcrossTwoYears_Is422_OnTheEnd()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        var response = await Ask(employee, vacation, "2026-12-30", "2027-01-05");

        (await ErrorsOn(response, "to")).Should().Contain("validation.leave_request.spans_two_years");
    }

    [Fact]
    public async Task APeriodWithNoWorkingDay_Is422_OnTheStart()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        // Sat 11, Sun 12 Jul.
        var response = await Ask(employee, vacation, "2026-07-11", "2026-07-12");

        (await ErrorsOn(response, "from")).Should().Contain("validation.leave_request.no_working_days");
    }

    [Fact]
    public async Task Hours_OnMoreThanOneDay_Are422_OnTheEnd()
    {
        var (employee, hours) = await EmployeeWithAllowanceAsync(days: 16m, unit: "Hours");

        var response = await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = hours, from = "2026-07-15", to = "2026-07-16", hours = 4m });

        (await ErrorsOn(response, "to")).Should().Contain("validation.leave_request.hours_are_one_day");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Hours_OutsideAWorkingDay_Are422(int asked)
    {
        var (employee, hours) = await EmployeeWithAllowanceAsync(days: 16m, unit: "Hours");

        var response = await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = hours, from = "2026-07-15", to = "2026-07-15", hours = (decimal)asked });

        (await ErrorsOn(response, "hours")).Should().Contain("validation.leave_request.hours_out_of_range");
    }

    /// <summary>A kind counted in hours takes a number of them: none is not zero hours of leave.</summary>
    [Fact]
    public async Task NoHours_OnAKindCountedInHours_Are422()
    {
        var (employee, hours) = await EmployeeWithAllowanceAsync(days: 16m, unit: "Hours");

        var response = await Ask(employee, hours, "2026-07-15", "2026-07-15");

        (await ErrorsOn(response, "hours")).Should().Contain("validation.leave_request.hours_out_of_range");
    }

    [Fact]
    public async Task Hours_OnAKindCountedInDays_Are422()
    {
        var (employee, vacation) = await EmployeeWithAllowanceAsync(days: 26m);

        var response = await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = vacation, from = "2026-07-15", to = "2026-07-15", hours = 4m });

        (await ErrorsOn(response, "hours")).Should().Contain("validation.leave_request.hours_on_a_kind_counted_in_days");
    }

    /// <summary>The control for the rules on the kind: one nobody defined is not found, not invalid.</summary>
    [Fact]
    public async Task AKindNobodyDefined_IsNotFound()
    {
        var (employee, _) = await EmployeeWithAllowanceAsync(days: 26m);

        var response = await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = Guid.NewGuid(), from = "2026-07-15", to = "2026-07-15", hours = 4m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PersonalHours_AreCountedInHours_OnOneDay()
    {
        var (employee, hours) = await EmployeeWithAllowanceAsync(days: 16m, unit: "Hours");

        var request = await ReadJsonAsync(await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = hours, from = "2026-07-15", to = "2026-07-15", hours = 4m }));

        request.GetProperty("amount").GetDecimal().Should().Be(4m);
    }

    /// <summary>
    ///     The kind is read once per submission: the rules that depend on it run on the row the
    ///     invoker loaded (<c>ValidateLoaded</c>), not on a second read in a validator.
    /// </summary>
    [Fact]
    public async Task ASubmission_ReadsTheKindOnce()
    {
        var (employee, hours) = await EmployeeWithAllowanceAsync(days: 16m, unit: "Hours");
        Sql.Clear();

        var response = await employee.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = hours, from = "2026-07-16", to = "2026-07-16", hours = 2m });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Sql.Commands.Count(c => c.Contains("FROM \"AbsenceKinds\"", StringComparison.Ordinal))
            .Should().Be(1, string.Join("\n---\n", Sql.Commands));
    }

    /// <summary>A kind that takes nothing from an allowance needs none.</summary>
    [Fact]
    public async Task SickLeave_NeedsNoAllowance()
    {
        var hired = await HireAsync();
        var sick = await DefineKindAsync(usesAllowance: false);
        var employee = await SignInAsync(hired.Account);

        var response = await Ask(employee, sick, "2026-11-02", "2026-11-04");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<(HttpClient Employee, Guid Kind)> EmployeeWithAllowanceAsync(decimal days, string unit = "Days")
    {
        var hired = await HireAsync();
        var kind = await DefineKindAsync(unit);
        var hr = await SignInAsHrAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = hired.Id, absenceKindId = kind, year = 2026, entitled = days }));

        return (await SignInAsync(hired.Account), kind);
    }

    private static Task<HttpResponseMessage> Ask(HttpClient employee, Guid kind, string from, string to) =>
        employee.PostAsJsonAsync("/api/leave-requests", new { absenceKindId = kind, from, to });

    /// <summary>The message keys a 422 names for <paramref name="field" />.</summary>
    private static async Task<IReadOnlyList<string?>> ErrorsOn(HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        return [.. JsonDocument.Parse(body).RootElement.GetProperty("errors").GetProperty(field)
            .EnumerateArray().Select(e => e.GetString())];
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code) ? code.GetString() : body;
    }
}
