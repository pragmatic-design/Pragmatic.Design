using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore;
using Pragmatic.Pipeline;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave.Dtos;
using TimeOff.Leave.Employees.Actions;
using static TimeOff.IntegrationTests.Infrastructure.TestCalendar;

namespace TimeOff.IntegrationTests;

/// <summary>
///     Erasing an employee leaves nothing that says who they were, anywhere in the database,
///     while the record of what was decided on their requests, and by whom, survives and still verifies.
/// </summary>
/// <remarks>
///     On a database of its own: verifying the trail needs sealed segments, and sealing the current hour
///     would refuse every later write to it — from every other class, on the shared database.
/// </remarks>
public sealed class ErasingAnEmployee(PostgresFixture database) : TimeOffTestBase(database)
{
    private string _connectionString = null!;

    protected override async Task<string> ConnectionStringAsync(PostgresFixture database) =>
        _connectionString = await database.CreateDatabaseAsync();

    [Fact]
    public async Task AfterErasure_NothingSaysWhoTheyWere_AndTheDecisionsStillVerify()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var leaver = await HireAsync(teamId: team.GetProperty("id").GetGuid());
        var sickLeave = await DefineKindAsync(usesAllowance: false);

        var member = await SignInAsync(leaver.Account);
        var profile = await ReadJsonAsync(await member.GetAsync("/api/me"));
        var employeeNumber = profile.GetProperty("employeeNumber").GetString()!;
        var request = (await ReadJsonAsync(await member.PostAsJsonAsync("/api/leave-requests",
            new { absenceKindId = sickLeave, from = Day(7), to = Day(8), reason = "Surgery on the left knee" })))
            .GetProperty("id").GetGuid();
        await ReadJsonAsync(await (await SignInAsync(manager.Account)).PostAsJsonAsync(
            $"/api/leave-requests/{request}/approve", new { note = "Get well soon" }));

        // The control: before the erasure the same search finds them, so it reads the tables and is not empty
        // by construction. And the email is only where the employee's own record keeps it — not in the rows
        // they wrote, their requests' access scopes or the audit trail, which know them by reference.
        // ⚠️ All three are classified, including the third: Identity_ExternalIdentityKey
        // is declared on LocalIdentity's BASE, and both the privacy reader and the redaction map read
        // what a type declares — so this list named a column nothing masked and nothing erased, beside
        // two that were, and said nothing about the difference.
        (await DatabaseText.FindAsync(_connectionString, leaver.Account.WorkEmail)).Should().BeEquivalentTo(
            ["Employees.WorkEmail", "Employees.Identity_Email", "Employees.Identity_ExternalIdentityKey"]);
        // ⚠️ The reason is NOT in any text column, before the erasure or after: it is encrypted under
        // this employee's own key (LeaveRequest.Reason is ErasureStrategy.DestroyKey), so the column
        // holds ciphertext — which is what makes a backup taken today unreadable after tomorrow's
        // erasure, and what clearing a column cannot do.
        //
        // ⚠️ This assertion alone is equally true of a value nobody ever encrypted. That it is readable
        // now and UNREADABLE AFTERWARDS is TheReason_ReadsBeforeTheErasure_AndIsErasedAfterIt, below;
        // The round trip is HandlingPersonalData.TheProtectedReason_IsReadThroughItsOwnEndpoint.
        (await DatabaseText.FindAsync(_connectionString, "Surgery on the left knee")).Should().BeEmpty(
            "a crypto-shredded column never holds the text, which is what makes a backup taken today "
            + "unreadable after tomorrow's erasure");

        // Personal data is erased after the relationship ends, not during it: the employee leaves
        // first, and the erasure has to reach the rows their leaving hid.
        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{leaver.Id}"));

        var erasure = await ReadJsonAsync(await hr.PostAsJsonAsync($"/api/employees/{leaver.Id}/erasure", new { }));

        erasure.GetProperty("isTotal").GetBoolean().Should().BeTrue();
        erasure.GetProperty("identityForgotten").GetBoolean().Should().BeTrue();
        erasure.GetProperty("erasedCount").GetInt32().Should().BeGreaterThanOrEqualTo(3,
            "the employee's row, their account and their request");

        // Nothing, in any text column of any table, still says who they were.
        foreach (var trace in new[] { leaver.Account.WorkEmail, leaver.Account.FullName, employeeNumber, "Surgery on the left knee", "Get well soon" })
            (await DatabaseText.FindAsync(_connectionString, trace)).Should().BeEmpty($"'{trace}' should be gone");

        // The account is removed, not closed in place: no identity key is left for it, not even a closed one
        // (its columns cannot take NULL, so closing it in place would rewrite them to "closed|…").
        (await DatabaseText.FindAsync(_connectionString, "closed|")).Should().BeEmpty();

        // No new session, and the one they had stops working.
        (await PostSignInAsync(leaver.Account.WorkEmail, leaver.Account.Password)).IsSuccessStatusCode.Should().BeFalse();
        (await member.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // What was decided, and by whom, is still there — and the trail still verifies.
        var decisions = await ReadJsonAsync(await hr.GetAsync($"/api/leave-requests/{request}/decisions"));
        decisions.GetArrayLength().Should().Be(1);
        decisions[0].GetProperty("operation").GetString().Should().Be("Leave.RequestApproved");
        decisions[0].GetProperty("decidedBy").GetString().Should().Be(manager.Id.ToString("N"));

        await SealAsIfTheHourHadPassedAsync();
        var integrity = await ReadJsonAsync(await hr.GetAsync("/api/compliance/audit-trail/integrity"));
        integrity.GetProperty("segmentsChecked").GetInt32().Should().BeGreaterThan(0, "an intact trail of nothing proves nothing");
        integrity.GetProperty("isIntact").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    ///     The reason reads while the employee is here and is <c>Erased</c> once they are
    ///     gone, which is the whole point of choosing <c>DestroyKey</c> over clearing the column.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Clearing a column erases what is in the database now. Destroying the key erases every
    ///         copy of the ciphertext that was ever taken, including the backup made this morning — and
    ///         this test is what asserts it: <see cref="AfterErasure_NothingSaysWhoTheyWere_AndTheDecisionsStillVerify" />
    ///         proves the plaintext is in no column, which is equally true of a value nobody ever
    ///         encrypted.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The control is a colleague of the same team, erased by nothing.</b> Without it,
    ///         "unreadable after the erasure" is satisfied by a key store that decrypts nothing at all,
    ///         and by an erasure that destroyed every key in the deployment.
    ///     </para>
    ///     <para>
    ///         ⚠️ Read the outcome and never the text. The generated host serialises with
    ///         <c>WhenWritingNull</c>, so an <c>Erased</c> answer has <b>no</b> <c>reason</c> property
    ///         at all — and <c>GetProperty</c> on an absent one throws a bare
    ///         <c>KeyNotFoundException</c>, which is what an earlier attempt at this test spent six
    ///         four-minute runs chasing inside the application.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheReason_ReadsBeforeTheErasure_AndIsErasedAfterIt()
    {
        var hr = await SignInAsHrAsync();
        var manager = await HireAsync(role: "Manager");
        var team = await ReadJsonAsync(await hr.PostAsJsonAsync("/api/teams",
            new { name = $"Team {Guid.NewGuid():N}"[..20], managerId = manager.Id }));
        var teamId = team.GetProperty("id").GetGuid();
        var sickLeave = await DefineKindAsync(usesAllowance: false);

        var leaver = await HireAsync(teamId: teamId);
        var colleague = await HireAsync(teamId: teamId);
        var theirs = await AskForLeaveAsync(leaver, sickLeave, "Surgery on the left knee");
        var theColleagues = await AskForLeaveAsync(colleague, sickLeave, "A death in the family");

        var reader = await SignInAsync(manager.Account);
        (await OutcomeOfAsync(reader, theirs)).Should().Be("Given");
        (await OutcomeOfAsync(reader, theColleagues)).Should().Be("Given");

        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{leaver.Id}"));
        var erasure = await ReadJsonAsync(await hr.PostAsJsonAsync($"/api/employees/{leaver.Id}/erasure", new { }));

        erasure.GetProperty("keyDestroyed").GetBoolean().Should().BeTrue(
            "the reason is erased by destroying the key, not by clearing the column");

        // The manager's token is older than the erasure, and reading is what it could already do: the
        // request row is still there, still in their team, and only its reason has become unreadable.
        (await OutcomeOfAsync(reader, theirs)).Should().Be("Erased");
        (await OutcomeOfAsync(reader, theColleagues)).Should().Be("Given",
            "the erasure destroyed one employee's key, not the deployment's ability to decrypt");
    }

    /// <summary>An employee asks for two days off, giving a reason only they and their manager may read.</summary>
    private async Task<Guid> AskForLeaveAsync(HiredEmployee employee, Guid absenceKind, string reason)
    {
        var client = await SignInAsync(employee.Account);

        return (await ReadJsonAsync(await client.PostAsJsonAsync("/api/leave-requests",
                new { absenceKindId = absenceKind, from = Day(7), to = Day(8), reason })))
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    ///     What a read of the reason says: <c>Given</c>, <c>NotGiven</c> or <c>Erased</c>.
    /// </summary>
    /// <remarks>
    ///     The outcome alone, because it is the only property always present — see the remark above on
    ///     <c>WhenWritingNull</c>.
    /// </remarks>
    private static async Task<string> OutcomeOfAsync(HttpClient reader, Guid leaveRequest)
        => (await ReadJsonAsync(await reader.GetAsync($"/api/leave-requests/{leaveRequest}/reason")))
            .GetProperty("outcome").GetString()!;

    /// <summary>
    ///     The erasure reaches an employee who has left when the application itself calls it, not only
    ///     over HTTP.
    /// </summary>
    /// <remarks>
    ///     The employee is preloaded (<c>[LoadEntity]</c>) past the soft-delete filter the action
    ///     lifts. Over HTTP the endpoint lifts it too, around the whole call, so the first test cannot see
    ///     whether the invoker does — and through the boundary, or from code as here, the preload read with
    ///     the filter on and answered 404 for the employee the erasure exists for.
    /// </remarks>
    [Fact]
    public async Task TheErasure_ReachesAnEmployeeWhoLeft_AlsoWhenTheApplicationCallsIt()
    {
        var hr = await SignInAsHrAsync();
        var leaver = await HireAsync();
        await ReadSuccessAsync(await hr.DeleteAsync($"/api/employees/{leaver.Id}"));

        using var scope = Services.CreateScope();
        var erase = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<EraseEmployeeAction, ErasureDto>>();
        using (scope.ServiceProvider.GetRequiredService<ICallContext>().EnterInternalCall())
        {
            var result = await erase.InvokeAsync(new EraseEmployeeAction { Id = leaver.Id });

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
            result.Value.IsTotal.Should().BeTrue();
        }
    }

    /// <summary>
    ///     What the host's sealer does once the hour and its grace period are over — run now, with a clock
    ///     two hours ahead, rather than waited for.
    /// </summary>
    private async Task SealAsIfTheHourHadPassedAsync()
    {
        using var scope = Services.CreateScope();
        var sealing = new AuditSealingService(
            scope.ServiceProvider.GetRequiredService<AuditDbContext>(),
            scope.ServiceProvider.GetRequiredService<IAuditSegmentNaming>(),
            new Later(TimeSpan.FromHours(2)));

        (await sealing.SealDueSegmentsAsync()).Should().NotBeEmpty();
    }

    private sealed class Later(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }
}
