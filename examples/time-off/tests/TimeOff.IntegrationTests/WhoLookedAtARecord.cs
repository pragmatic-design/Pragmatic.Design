using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     Reading another person's record is recorded; reading your own is not.
/// </summary>
/// <remarks>
///     <para>
///         The trail is written by an interceptor over <c>SaveChanges</c>, so it sees every change and
///         no query at all. That is the right amount for most operations — reads outnumber writes by
///         orders of magnitude, and a trail holding all of them is one nobody can search when it
///         matters — and it is not enough for "who looked at this employee", which cannot be answered
///         afterwards. <c>[RecordAccess]</c> on <c>GetEmployeeQuery</c> is how that one operation says
///         so.
///     </para>
///     <para>
///         ⚠️ What is recorded is the operation, the actor and the time — never the row. An access
///         record holding the data it accounts for is a second copy of what it exists to protect.
///     </para>
/// </remarks>
public sealed class WhoLookedAtARecord(PostgresFixture database) : TimeOffTestBase(database)
{
    private const string PersonalDataRead = "Privacy.PersonalDataRead";

    [Fact]
    public async Task ReadingAnEmployeesRecord_IsRecorded()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var mark = await HighWaterAsync();

        await ReadSuccessAsync(await hr.GetAsync($"/api/employees/{employee.Id}"));

        var recorded = await ReadsSinceAsync(mark);

        recorded.Should().ContainSingle();
        recorded[0].BusinessOperation.Should().Be("TimeOff.Leave.Employees.Queries.GetEmployeeQuery");
        recorded[0].TargetType.Should().Be("Employee");
        recorded[0].Category.Should().Be(AuditCategory.Privacy);
    }

    /// <summary>
    ///     The control: an employee reading their own profile is not an access anybody answers for.
    /// </summary>
    /// <remarks>
    ///     Without it, "the read is recorded" is satisfied by recording every read — which is the
    ///     setting nobody can afford to leave on, and the reason the attribute is per operation. It also
    ///     catches the other direction: a trail that recorded nothing would pass the first test's
    ///     counterpart only by accident.
    /// </remarks>
    [Fact]
    public async Task ReadingYourOwnProfile_IsNotRecorded()
    {
        var employee = await HireAsync();
        var member = await SignInAsync(employee.Account);
        var mark = await HighWaterAsync();

        await ReadSuccessAsync(await member.GetAsync("/api/me"));

        (await ReadsSinceAsync(mark)).Should().BeEmpty("GetMyProfileQuery carries no [RecordAccess]");
    }

    /// <summary>
    ///     The access record names who looked, and does not carry what they saw.
    /// </summary>
    [Fact]
    public async Task TheRecord_NamesTheActor_AndNotTheRow()
    {
        var hr = await SignInAsHrAsync();
        var employee = await HireAsync();
        var mark = await HighWaterAsync();

        await ReadSuccessAsync(await hr.GetAsync($"/api/employees/{employee.Id}"));

        var entry = (await ReadsSinceAsync(mark)).Should().ContainSingle().Which;

        entry.ActorRef.Should().NotBeNullOrWhiteSpace("somebody looked, and the trail says who");
        entry.ActorRef.Should().NotContain(TestAccounts.FirstAdministrator.WorkEmail,
            "the actor is a reference, not an address: the trail is append-only and no erasure reaches it");
    }

    /// <summary>The trail's sequence as it stands, so what follows can be told from what was there.</summary>
    /// <remarks>
    ///     ⚠️ <c>Seq</c>, and not set difference on the entries. <c>AuditEntry</c> is a class with
    ///     reference equality, so <c>Except</c> between two queries matches nothing and reports every
    ///     entry as new — which is what the first version of these tests did, reporting 11 new reads
    ///     where there was one. The database shares itself with the rest of the suite and the trail is
    ///     append-only, so the sequence is the honest boundary.
    /// </remarks>
    private async Task<long> HighWaterAsync()
    {
        var entries = await PrivacyEntriesAsync();
        return entries.Count == 0 ? 0 : entries.Max(e => e.Seq);
    }

    /// <summary>The personal-data reads recorded after <paramref name="seq" />.</summary>
    private async Task<IReadOnlyList<AuditEntry>> ReadsSinceAsync(long seq) =>
        [.. (await PrivacyEntriesAsync()).Where(e => e.Seq > seq && e.Operation == PersonalDataRead)];

    private async Task<IReadOnlyList<AuditEntry>> PrivacyEntriesAsync()
    {
        using var scope = Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { Category = AuditCategory.Privacy, Limit = 1000 });

        return page.Entries;
    }
}
