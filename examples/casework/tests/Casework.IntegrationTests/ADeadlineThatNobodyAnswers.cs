using System.Net.Http.Json;
using Casework.Intake;
using Casework.Intake.Infrastructure.Jobs;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     A verification nobody answers expires on its own, and the schedule that expires it is
///     a row.
/// </summary>
/// <remarks>
///     <para>
///         <b>The clock is sealed</b>, which is what makes this a test rather than a ten-day wait: Intake
///         runs with a <see cref="TestClock" /> and the work is driven through
///         <c>IExpireOverdueVerifications</c>, which takes the instant as an argument. Going through the
///         scheduler instead would assert the polling interval and the lease as well as the expiry — so
///         the schedule's own existence is asserted separately, by reading its
///         row.
///     </para>
///     <para>
///         ⚠️ The answer that arrives <b>after</b> the expiry is recorded and decides nothing, and that
///         is the choice this story had to make and assert. It is the state machine's: there is no move
///         from <c>Expired</c> to <c>Approved</c>. So a late outcome is a fact on the case — an operator
///         can see what came back — and the case stays expired until somebody asks for a verification
///         again.
///     </para>
/// </remarks>
public sealed class ADeadlineThatNobodyAnswers(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>The instant the case is opened at. Everything else is measured from it.</summary>
    private static readonly DateTimeOffset Asked = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Asked);

    /// <summary>
    ///     Intake runs on the suite's clock: <c>[FromClock]</c> on the operations reads it, so the case's
    ///     deadline is ten days after <see cref="Asked" /> and not ten days after now.
    /// </summary>
    protected override void ConfigureIntake(IServiceCollection services)
        => services.AddSingleton<IClock>(_clock);

    [Fact]
    public async Task PastItsDeadline_TheCaseStopsWaiting()
    {
        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });

        var waiting = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        waiting.GetProperty("status").GetString().Should().Be("InVerification");

        // Nothing expires while the window is open — the control that comes first, because a sweep that
        // expired everything would pass the assertion below.
        (await SweepAsync(Asked.AddDays(9))).Should().Be(0,
            "a case inside its window is what the process is for, not what the sweep is for");

        (await SweepAsync(Asked.AddDays(11))).Should().Be(1, "ten days passed and nobody answered");

        var expired = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        expired.GetProperty("status").GetString().Should().Be("Expired",
            "a case cannot sit in InVerification for ever because a message never came");

        // And it is not expired twice: the deadline is cleared, so the next run does not find it.
        (await SweepAsync(Asked.AddDays(12))).Should().Be(0,
            "expiring clears the deadline, so a case is expired once and the sweep is idempotent");
    }

    /// <summary>
    ///     The answer that arrives after the expiry is recorded and decides nothing.
    /// </summary>
    [Fact]
    public async Task AnAnswerAfterTheExpiry_IsRecordedAndDecidesNothing()
    {
        await WaitForSubscriberAsync("verification-answered");

        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsJsonAsync($"api/cases/{id}/verifications", new { kind = "identity" });
        (await SweepAsync(Asked.AddDays(11))).Should().Be(1, "the deadline passed first");

        // The other service answers late — which is exactly what a deadline exists for.
        await PublishFromVerifyAsync(new Casework.Verify.Events.VerificationAnswered(
            Guid.NewGuid(), id, Casework.Verify.Events.VerificationOutcome.Passed, Asked.AddDays(12)));

        await EventuallyAsync(
            async () => (await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}")))
                .TryGetProperty("verificationOutcome", out _),
            "the late answer was recorded as a fact — an operator can see what came back");

        var @case = await ReadJsonAsync(await operator1.GetAsync($"api/cases/{id}"));
        @case.GetProperty("verificationOutcome").GetString().Should().Be("Passed");
        @case.GetProperty("status").GetString().Should().Be("Expired",
            "and it revived nothing: there is no move from Expired to Approved, so the decision is "
            + "refused by the state machine rather than by an if somebody remembered to write");
    }

    /// <summary>
    ///     The schedule is durable: its tables exist and its definition is a row with a next occurrence.
    /// </summary>
    /// <remarks>
    ///     Read with SQL, because "registered" and "scheduled in memory" are indistinguishable from the
    ///     outside and only one of them survives a restart. Through the <b>registrar</b> — the very thing
    ///     the scheduler calls — rather than by waiting for the scheduler's first pass.
    /// </remarks>
    [Fact]
    public async Task TheSchedule_IsARowWithItsNextOccurrence()
    {
        var tables = await TablesAsync(IntakeConnectionString);
        tables.Should().Contain("__Jobs", "a job enqueued and not yet run has to survive a restart");
        tables.Should().Contain("__RecurringJobs", "and so does the schedule an operator reads");

        IntakeServices.GetRequiredService<IRecurringJobStore>().Should().BeOfType<EfCoreRecurringJobStore>(
            "UseEfCore() + UseEfCorePersistence() asked for the durable store, and a silent fall back to "
            + "memory is what ships an application without the persistence it asked for");

        using var scope = IntakeServices.CreateScope();
        var registrar = scope.ServiceProvider.GetRequiredService<IRecurringJobRegistrar>();
        foreach (var definition in scope.ServiceProvider.GetServices<IRecurringJobProvider>()
                     .SelectMany(provider => provider.GetDefinitions()))
        {
            (await registrar.RegisterAsync(definition)).Should().BeTrue();
        }

        var (cron, next) = await ScheduleAsync("expire-verifications");

        cron.Should().Be("0 * * * *", "the cron the [RecurringJob] declares, persisted as declared");
        next.Should().NotBeNull(
            "the first occurrence is seeded on registration — a schedule with no next execution is a "
            + "definition the poll never returns");
    }

    /// <summary>Runs the sweep as the job would, at an instant this test chooses.</summary>
    private async Task<int> SweepAsync(DateTimeOffset now)
    {
        using var scope = IntakeServices.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IExpireOverdueVerifications>()
            .RunAsync(now);
    }

    /// <summary>The persisted schedule, read with SQL: what an operator sees, not what a store remembers.</summary>
    private async Task<(string Cron, DateTimeOffset? Next)> ScheduleAsync(string id)
    {
        await using var connection = new NpgsqlConnection(IntakeConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """select "CronExpression", "NextExecutionAt" from "__RecurringJobs" where "Id" = @id""",
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"'{id}' should have a row of its own");

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task PublishFromVerifyAsync(Casework.Verify.Events.VerificationAnswered answered)
    {
        using var scope = VerifyServices.CreateScope();

        await AsTenantAsync(async () =>
        {
            await scope.ServiceProvider
                .GetRequiredService<Pragmatic.Messaging.IMessageBus>()
                .PublishAsync(answered);

            return true;
        });
    }

    private static async Task<Guid> ACaseAsync(HttpClient caller)
    {
        var created = await ReadJsonAsync(await caller.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));

        return created.GetProperty("id").GetGuid();
    }
}
