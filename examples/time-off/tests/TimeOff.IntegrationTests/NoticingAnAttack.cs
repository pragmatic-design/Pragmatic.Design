using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Privacy;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;
using TimeOff.Leave.Entities;

namespace TimeOff.IntegrationTests;

/// <summary>
///     Repeated failed sign-ins reach the audit trail, and a compliance read turns them into an
///     incident whose reporting clock is already running.
/// </summary>
/// <remarks>
///     <para>
///         Two packages meet here and neither knows about the other. <c>Identity.Auditing</c> listens to
///         <c>LoginFailed</c> and writes a <c>Security.LoginFailed</c> entry; <c>Incidents.Audit</c>
///         reads the trail back and counts. The host joins them by referencing both — the module never
///         asked to be audited.
///     </para>
///     <para>
///         ⚠️ <b>Nothing here decides whether an incident is notifiable.</b> Every incident comes back at
///         <c>Detected</c> and the framework goes no further: that judgement is about impact, and
///         automating it produces both kinds of error — noise, and silence about the one that mattered.
///         What is automated is the arithmetic against a deadline that started at a moment nobody wrote
///         down.
///     </para>
///     <para>
///         On a database of its own: the detector counts entries in a window across the whole trail, and
///         on the shared database every other class's failed sign-in would be counted too. The
///         thresholds below would then measure the suite rather than the attack.
///     </para>
/// </remarks>
public sealed class NoticingAnAttack(PostgresFixture database) : TimeOffTestBase(database)
{
    private string _connectionString = null!;

    protected override async Task<string> ConnectionStringAsync(PostgresFixture database) =>
        _connectionString = await database.CreateDatabaseAsync();

    [Fact]
    public async Task RepeatedFailedSignIns_BecomeAnIncidentWithItsClockRunning()
    {
        var target = await HireAsync();
        await FailToSignInAsync(target.Account.WorkEmail, times: 5);

        var incidents = await ScanAsync(threshold: 5);

        // ⚠️ Two, and that is the point: the employee has a pseudonym from the moment HR
        // registers them, so the same burst is seen by the per-subject rule as well as by the global
        // one. It would be one if an account nobody had signed into yet could not be attributed.
        incidents.Should().HaveCount(2);

        var incident = incidents.Should().ContainSingle(i => Summary(i).Contains("across the system")).Which;

        Summary(incident).Should().Contain("5 × Security.LoginFailed");
        incident.GetProperty("stage").GetString().Should().Be("Detected",
            "the framework notices; whether it is notifiable is somebody's judgement");
        incident.GetProperty("nextObligation").GetString().Should().Be("early warning");
        incident.GetProperty("hoursRemaining").GetDouble().Should().BeGreaterThan(23).And.BeLessThan(24.1,
            "the NIS2 early warning is 24 hours from detection");
    }

    /// <summary>
    ///     An attack on an account this application knows is attributed to that account, and the
    ///     incident names the pseudonym rather than the person.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Two things have to hold for the burst to be attributed per subject</b>, and both
    ///         are needed:
    ///     </para>
    ///     <para>
    ///         <b>Who the subject is.</b> The bridge does not look it up under a fixed
    ///         <c>("User", &lt;login e-mail&gt;)</c>: it asks the application, through
    ///         <c>ISecuritySubjectLocator</c>, and this host names
    ///         <see cref="TimeOff.Host.Identity.TheEmployeeASignInWasAbout" /> at the call site, which
    ///         answers with the pair <c>[DataSubject(nameof(EmployeeNumber))]</c> asks for.
    ///     </para>
    ///     <para>
    ///         <b>Whether that subject exists yet.</b> The reference is allocated when HR registers
    ///         them. Allocated on first sight — first successful sign-in, an export, an erasure — an
    ///         employee who had never signed in would have none, the located key would resolve to
    ///         nothing, and the entry would be written unattributed: precisely the account an attacker
    ///         works on. The bridge cannot allocate it for itself: its input is an address a stranger
    ///         typed, and pseudonymising that would let anyone fill the registry by guessing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AttemptsAgainstAKnownAccount_AreAttributedToIt()
    {
        var target = await HireAsync();
        await FailToSignInAsync(target.Account.WorkEmail, times: 5);

        var incidents = await ScanAsync(threshold: 5);

        var incident = incidents.Should()
            .ContainSingle(i => Summary(i).Contains("against one account"),
                "the locator names the employee and the employee has a reference to be named by").Which;

        // ⚠️ That subject and not merely a subject: an incident naming somebody else's pseudonym would
        // read as an attack correctly detected and send whoever acts on it to the wrong account.
        Summary(incident).Should().Contain(await PseudonymOfAsync(target.Id));
        Summary(incident).Should().NotContain(target.Account.WorkEmail,
            "an incident names the pseudonym; the address is the thing the pseudonym exists to keep out "
            + "of every record that outlives it");
    }

    /// <summary>
    ///     The lockout the fifth attempt causes is attributed to the same account, end to end.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>The lockout is the one that has to resolve</b>, by its handler's own words: "a
    ///         lockout always concerns an account that exists, so unlike a failed login it should
    ///         resolve to a subject".
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>And it takes a different path through the locator than a failed sign-in does.</b>
    ///         A failed sign-in names what was typed; a lockout names the identity record by its
    ///         composed <c>{issuer}|{subject}</c> key, which this application has to split and unescape
    ///         before it can look an employee up — a branch of
    ///         <see cref="TimeOff.Host.Identity.TheEmployeeASignInWasAbout" /> that the unit suite
    ///         exercises only against a fake locator. A mistake there
    ///         is silent: the entry is still written, without a subject.
    ///     </para>
    ///     <para>
    ///         Read through the trail by pseudonym rather than through an incident: the per-subject
    ///         incident rule counts <c>Security.LoginFailed</c> and would be green whether or not the
    ///         lockout resolved.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheLockoutTheLastAttemptCauses_IsAttributedToTheSameAccount()
    {
        var target = await HireAsync();

        // Five is the lockout threshold as well as the incident one: the fifth failure locks the account.
        await FailToSignInAsync(target.Account.WorkEmail, times: 5);

        var pseudonym = await PseudonymOfAsync(target.Id);
        pseudonym.Should().NotBeNullOrEmpty("the employee has had a reference since HR registered them");

        using var scope = Services.CreateScope();
        var entries = (await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { SubjectRef = pseudonym, Limit = 50 })).Entries;

        entries.Should().Contain(e => e.Operation == "Security.AccountLocked",
            "the lockout names the identity record, and this application can say which employee that is");
        entries.Should().Contain(e => e.Operation == "Security.LoginFailed",
            "the attempts that caused it are attributed to the same account, by the other path through "
            + "the locator");
    }

    /// <summary>
    ///     The control: below the threshold there is no incident, so the scan is counting and not
    ///     reporting whatever it finds.
    /// </summary>
    /// <remarks>
    ///     Without it, "an incident is raised" is satisfied by raising one for every failed sign-in —
    ///     which is the alarm nobody reads, and the reason the thresholds are this deployment's numbers
    ///     rather than the framework's.
    /// </remarks>
    [Fact]
    public async Task BelowTheThreshold_NothingIsRaised()
    {
        var target = await HireAsync();
        await FailToSignInAsync(target.Account.WorkEmail, times: 2);

        var incidents = await ScanAsync(threshold: 5);

        incidents.Should().BeEmpty();
    }

    /// <summary>
    ///     Attempts against an account that does not exist are caught by the global rule and by nothing
    ///     else.
    /// </summary>
    /// <remarks>
    ///     Those entries carry no subject by design — pseudonymising an address a stranger typed would
    ///     let anyone fill the subject registry — so per-subject counting groups them under nothing.
    ///     Enumerating accounts that do not exist is exactly a spraying pattern, which is why the action
    ///     configures both rules and not the per-subject one alone.
    /// </remarks>
    [Fact]
    public async Task AttemptsAgainstAccountsThatDoNotExist_AreCaughtByTheGlobalRule()
    {
        await FailToSignInAsync($"nobody.{Guid.NewGuid():N}@time-off.test", times: 3);
        await FailToSignInAsync($"nobody.{Guid.NewGuid():N}@time-off.test", times: 3);

        var incidents = await ScanAsync(threshold: 5);

        var incident = incidents.Should().ContainSingle().Which;
        Summary(incident).Should().Contain("across the system");
    }

    private static string Summary(JsonElement incident) => incident.GetProperty("summary").GetString()!;

    /// <summary>
    ///     The reference this application knows an employee by — the pair its registry actually holds.
    /// </summary>
    /// <remarks>
    ///     Read from the registry rather than assumed, because the whole issue behind these tests was
    ///     two halves of the system disagreeing about what that pair is.
    /// </remarks>
    private async Task<string> PseudonymOfAsync(Guid employeeId)
    {
        var hr = await SignInAsHrAsync();
        var number = (await ReadJsonAsync(await hr.GetAsync($"/api/employees/{employeeId}")))
            .GetProperty("employeeNumber").GetString()!;

        using var scope = Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISubjectRegistry>()
            .FindReferenceAsync(nameof(Employee), number) ?? string.Empty;
    }

    private async Task<List<JsonElement>> ScanAsync(int threshold)
    {
        var hr = await SignInAsHrAsync();
        var body = await ReadJsonAsync(
            await hr.GetAsync($"/api/compliance/security-incidents?threshold={threshold}"));

        return [.. body.EnumerateArray()];
    }

    /// <summary>Signs in wrongly <paramref name="times" /> times, as somebody guessing would.</summary>
    private async Task FailToSignInAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
            await PostSignInAsync(email, $"wrong-{i}");
    }
}
