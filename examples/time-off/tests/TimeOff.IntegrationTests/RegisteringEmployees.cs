using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     HR registers employees: a number like <c>EMP-00001</c>, a unique work email, an audit
///     trail, and an invitation to choose a password.
/// </summary>
public sealed class RegisteringEmployees(PostgresFixture database) : TimeOffTestBase(database)
{
    private const string EmployeeNumber = @"^EMP-\d{5}$";

    [Fact]
    public async Task HrRegistersAnEmployee_AndReadsItBack_WithItsNumberAndWhoRegisteredThem()
    {
        var hr = await SignInAsHrAsync();
        var email = $"{Guid.NewGuid():N}@time-off.test";

        var created = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/employees",
            new { fullName = "Giulia Bianchi", workEmail = email, hiredOn = "2023-09-01" }));

        var id = created.GetProperty("id").GetGuid();
        var employee = await ReadJsonAsync(await hr.GetAsync($"/api/employees/{id}"));

        employee.GetProperty("employeeNumber").GetString().Should().MatchRegex(EmployeeNumber);
        employee.GetProperty("employeeNumber").GetString().Should().Be(created.GetProperty("employeeNumber").GetString());
        employee.GetProperty("fullName").GetString().Should().Be("Giulia Bianchi");
        employee.GetProperty("role").GetString().Should().Be("Employee", "the role an employee gets unless HR says otherwise");
        employee.GetProperty("hiredOn").GetString().Should().Be("2023-09-01");
        employee.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        employee.GetProperty("createdBy").GetString().Should().NotBeNullOrEmpty("the audit trail says who registered them");
    }

    [Fact]
    public async Task TwoEmployees_GetDifferentNumbers()
    {
        var first = await HireAsync();
        var second = await HireAsync();
        var hr = await SignInAsHrAsync();

        var a = await ReadJsonAsync(await hr.GetAsync($"/api/employees/{first.Id}"));
        var b = await ReadJsonAsync(await hr.GetAsync($"/api/employees/{second.Id}"));

        a.GetProperty("employeeNumber").GetString().Should().NotBe(b.GetProperty("employeeNumber").GetString());
    }

    /// <summary>
    ///     The same address in another case is the same address: the account's sign-in name would
    ///     otherwise belong to two people.
    /// </summary>
    [Fact]
    public async Task ASecondEmployeeWithTheSameWorkEmail_IsRefusedWith409_AndSaysWhy()
    {
        var hr = await SignInAsHrAsync();
        var email = $"{Guid.NewGuid():N}@time-off.test";
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/employees",
            new { fullName = "First Person", workEmail = email, hiredOn = "2023-09-01" }));

        var response = await hr.PostAsJsonAsync("/api/employees",
            new { fullName = "Second Person", workEmail = email.ToUpperInvariant(), hiredOn = "2024-01-15" });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("\"code\":\"DB_CONFLICT\"", "the conflict names itself, so a client can tell it from any other");
    }

    /// <summary>
    ///     The conflict says <em>which</em> field collided, which is what a form can act on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ PostgreSQL leaves <c>pgEx.ColumnName</c> empty for a unique violation, filling
    ///         <c>ConstraintName</c> and <c>TableName</c> instead. Taking the field from the column name
    ///         alone gives "Duplicate value violates unique constraint." and names nothing.
    ///     </para>
    ///     <para>
    ///         So the constraint name is kept, and <c>RuleViolationClassifier</c> — which has the model —
    ///         turns the index's name into the property it covers. <b>A constraint name is not a field name</b>: a client
    ///         told <c>IX_Employees_WorkEmail</c> is no better off than one told nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheConflict_NamesTheFieldThatCollided()
    {
        var hr = await SignInAsHrAsync();
        var email = $"{Guid.NewGuid():N}@time-off.test";
        await ReadJsonAsync(await hr.PostAsJsonAsync("/api/employees",
            new { fullName = "First Person", workEmail = email, hiredOn = "2023-09-01" }));

        var response = await hr.PostAsJsonAsync("/api/employees",
            new { fullName = "Second Person", workEmail = email, hiredOn = "2024-01-15" });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Duplicate value for WorkEmail.",
            "the model turns the violated index into the property it covers");
        body.Should().NotContain("IX_",
            "and the caller gets the field, not the database's name for the index that holds it");
    }

    [Fact]
    public async Task TheNewEmployee_IsInvited_AndSignsInWithThePasswordTheyChose()
    {
        var hired = await HireAsync();

        var profile = await ReadJsonAsync(await (await SignInAsync(hired.Account)).GetAsync("/api/me"));

        profile.GetProperty("id").GetGuid().Should().Be(hired.Id);
        profile.GetProperty("workEmail").GetString().Should().Be(hired.Account.WorkEmail);
    }

    /// <summary>The invitation is spent: a second use of the same token is refused.</summary>
    [Fact]
    public async Task TheInvitation_WorksOnce()
    {
        var hired = await HireAsync();

        var again = await AcceptInvitationAsync(hired.Account.WorkEmail, "Another-Pa55word!");

        again.IsSuccessStatusCode.Should().BeFalse(await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnEmployee_CannotRegisterEmployees()
    {
        var employee = await SignInAsync((await HireAsync()).Account);

        var response = await employee.PostAsJsonAsync("/api/employees",
            new { fullName = "Someone", workEmail = $"{Guid.NewGuid():N}@time-off.test", hiredOn = "2024-01-15" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithoutSigningIn_NobodyRegistersEmployees()
    {
        var response = await Client.PostAsJsonAsync("/api/employees",
            new { fullName = "Someone", workEmail = $"{Guid.NewGuid():N}@time-off.test", hiredOn = "2024-01-15" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HrCreatesATeam_WithAManager_AndPutsAnEmployeeInIt()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");

        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var teamId = team.GetProperty("id").GetGuid();
        var member = await HireAsync(teamId: teamId);

        var read = await ReadJsonAsync(await hr.GetAsync($"/api/employees/{member.Id}"));

        team.GetProperty("managerId").GetGuid().Should().Be(manager.Id);
        read.GetProperty("teamId").GetGuid().Should().Be(teamId);
    }

    [Fact]
    public async Task HrHandsATeamToAnotherManager()
    {
        var hr = await SignInAsHrAsync();
        var first = await HireAsync(role: "Manager");
        var second = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = first.Id }));
        var teamId = team.GetProperty("id").GetGuid();

        var updated = await ReadJsonAsync(await hr.PutAsJsonAsync($"/api/teams/{teamId}", new { managerId = second.Id }));

        updated.GetProperty("managerId").GetGuid().Should().Be(second.Id);
        updated.GetProperty("name").GetString().Should().Be(team.GetProperty("name").GetString(),
            "what was not sent does not change");
    }

    /// <summary>
    ///     The hand-over reads the employees table once: the outgoing manager comes with the
    ///     team, through the include the mutation declares, instead of a read written by hand.
    /// </summary>
    /// <remarks>
    ///     The rule this replaces was going to be inferred — "a load whose key is the FK of a relation the
    ///     mutation does not change is the navigation" — and on this very mutation it would have picked the
    ///     wrong row: <c>ManagerId</c> names the <b>incoming</b> manager and the navigation holds the
    ///     outgoing one, so the hand-over would have silently done nothing.
    /// </remarks>
    [Fact]
    public async Task HandingOverATeam_ReadsTheEmployeesOnce()
    {
        var hr = await SignInAsHrAsync();
        var first = await HireAsync(role: "Manager");
        var second = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = first.Id }));
        var teamId = team.GetProperty("id").GetGuid();
        Sql.Clear();

        var response = await hr.PutAsJsonAsync($"/api/teams/{teamId}", new { managerId = second.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        // ⚠️ The predicate is a read of one employee by key, not "the word Employees appears": the team's
        // own query now joins the table for the include, and the request's authentication reads the caller
        // by external key. Counting the table name gave the same number before and after the change — a
        // test that would have stayed red while measuring nothing.
        Sql.Commands.Count(c => c.Contains("e.\"PersistenceId\" = @id", StringComparison.Ordinal))
            .Should().Be(1, string.Join("\n---\n", Sql.Commands));
    }

    /// <summary>No manager sent, none loaded: the rename leaves the manager where it was.</summary>
    [Fact]
    public async Task HrRenamesATeam_AndTheManagerStays()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var teamId = team.GetProperty("id").GetGuid();
        var renamed = $"Renamed {Guid.NewGuid():N}"[..20];

        var updated = await ReadJsonAsync(await hr.PutAsJsonAsync($"/api/teams/{teamId}", new { name = renamed }));

        updated.GetProperty("name").GetString().Should().Be(renamed);
        updated.GetProperty("managerId").GetGuid().Should().Be(manager.Id);
    }

    /// <summary>A manager sent is a manager that must exist: "load it if given" is not "ignore it if wrong".</summary>
    [Fact]
    public async Task HandingATeamToNobodyWeKnow_IsNotFound()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var name = $"Team {Guid.NewGuid():N}"[..20];
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams", new { name, managerId = manager.Id }));
        var teamId = team.GetProperty("id").GetGuid();

        var response = await hr.PutAsJsonAsync($"/api/teams/{teamId}", new { managerId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        var page = await ReadJsonAsync(await hr.GetAsync($"/api/teams?name={Uri.EscapeDataString(name)}"));
        page.GetProperty("items").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == teamId)
            .GetProperty("managerId").GetGuid().Should().Be(manager.Id, "the refused update changed nothing");
    }
}
