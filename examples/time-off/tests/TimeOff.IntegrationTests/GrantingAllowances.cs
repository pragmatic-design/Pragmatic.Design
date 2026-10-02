using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR grants each employee an allowance per kind and year,
///     one and only one.
/// </summary>
public sealed class GrantingAllowances(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task HrGrantsAnAllowance_ForAnEmployeeAKindAndAYear()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var kind = await DefineKindAsync();

        var allowance = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2026, entitled = 26m, carriedOver = 3.5m }));

        allowance.GetProperty("entitled").GetDecimal().Should().Be(26m);
        allowance.GetProperty("carriedOver").GetDecimal().Should().Be(3.5m);
    }

    [Fact]
    public async Task ASecondGrant_ForTheSameEmployeeKindAndYear_IsAConflict()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var kind = await DefineKindAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2026, entitled = 26m }));

        var again = await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2026, entitled = 30m });

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync());
    }

    /// <summary>The control: the key is the year too.</summary>
    [Fact]
    public async Task AnotherYear_IsAnotherAllowance()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var kind = await DefineKindAsync();
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2026, entitled = 26m }));

        var next = await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2027, entitled = 26m });

        next.StatusCode.Should().Be(HttpStatusCode.Created, await next.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HrCorrectsAnAllowance()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var kind = await DefineKindAsync();
        var granted = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = kind, year = 2026, entitled = 26m, carriedOver = 2m }));

        var corrected = await ReadJsonAsync(await hr.PutAsJsonAsync(
            $"/api/allowances/{granted.GetProperty("id").GetGuid()}", new { entitled = 28m }));

        corrected.GetProperty("entitled").GetDecimal().Should().Be(28m);
        corrected.GetProperty("carriedOver").GetDecimal().Should().Be(2m, "what was not sent does not change");
    }

    /// <summary>
    ///     A kind nobody defined is a 404 naming it — checked with <c>[RequireExists]</c> before the write — not the
    ///     database's foreign-key violation.
    /// </summary>
    [Fact]
    public async Task AGrantOfAKindThatDoesNotExist_IsNotFound_NamingIt()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var nobody = Guid.NewGuid();

        var response = await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = employee.Id, absenceKindId = nobody, year = 2026, entitled = 26m });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain(nobody.ToString());
    }

    /// <summary>The same for the employee.</summary>
    [Fact]
    public async Task AGrantForAnEmployeeWhoDoesNotExist_IsNotFound_NamingThem()
    {
        var hr = await SignInAsHrAsync();
        var kind = await DefineKindAsync();
        var nobody = Guid.NewGuid();

        var response = await hr.PostAsJsonAsync("/api/allowances",
            new { employeeId = nobody, absenceKindId = kind, year = 2026, entitled = 26m });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain(nobody.ToString());
    }

    [Fact]
    public async Task AnEmployee_CannotGrantThemselvesAnAllowance()
    {
        var hired = await HireAsync();
        var kind = await DefineKindAsync();
        var employee = await SignInAsync(hired.Account);

        var response = await employee.PostAsJsonAsync("/api/allowances",
            new { employeeId = hired.Id, absenceKindId = kind, year = 2026, entitled = 365m });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
