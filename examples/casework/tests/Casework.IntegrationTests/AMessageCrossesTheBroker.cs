using Casework.IntegrationTests.Infrastructure;
using Casework.Intake.Events;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     Intake publishes, RabbitMQ carries it, and Verify's handler writes a row in <b>its</b>
///     database.
/// </summary>
/// <remarks>
///     <para>
///         What makes this test worth its container: the assertion is a row read out of the <b>other</b>
///         service's database with SQL. A harness counter, a log line or a handler that merely ran would
///         all be satisfied by an in-process bus.
///     </para>
///     <para>
///         ⚠️ The publish goes through Intake's own <c>IMessageBus</c>, resolved from Intake's container,
///         rather than through an HTTP operation. What is proved here is the transport and the consumer
///         in another process, and nothing else; the operation that publishes in the application, through
///         the outbox, is <c>TheOutboxDeliversWhatItHeld</c>'s.
///     </para>
/// </remarks>
public sealed class AMessageCrossesTheBroker(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    [Fact]
    public async Task PublishedByIntake_ItIsHandledByVerify_WhichWritesItsOwnRow()
    {
        var caseId = Guid.NewGuid();

        // Verify's consumer binds its queue in the background after it connects; a message published
        // before that is discarded by the exchange, which would fail this test for a reason no
        // deployment has.
        await WaitForSubscriberAsync("verification-requested");

        await PublishAsync(new VerificationRequested(
            caseId, "identity", DateTimeOffset.UtcNow.AddDays(10), DateTimeOffset.UtcNow));

        await EventuallyAsync(
            async () => await VerificationsForAsync(caseId) == 1,
            $"Verify wrote a verification for case {caseId} — the message never arrived");

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "Kind" from "Verifications" where "CaseId" = '{caseId}'"""))
            .Should().Be("identity", "what crossed the broker is the message's content, not only its arrival");
    }

    /// <summary>
    ///     The control: a message no handler subscribes to writes nothing. It is what rules out the row
    ///     appearing for any reason other than this handler.
    /// </summary>
    [Fact]
    public async Task AMessageNobodyHandles_WritesNothing()
    {
        var caseId = Guid.NewGuid();

        await PublishAsync(new NobodyHandlesThis(caseId));
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await VerificationsForAsync(caseId)).Should().Be(0,
            "a verification row appears because a handler wrote it, not because a message was published");
    }

    /// <summary>Publishes through the application's own bus, in the application's own scope.</summary>
    /// <remarks>
    ///     ⚠️ <b>Inside a tenant</b>: a message published with none is refused by the consumer rather
    ///     than written into the shared database (<c>RecordTheVerificationRequest</c>). Were it not
    ///     refused, a test publishing without one would pass by reading a row that belongs to nobody.
    ///     An operation always runs inside a tenant, so publishing inside one is
    ///     what production does; the no-tenant case has a test of its own in
    ///     <c>TheTenantTravelsOnTheMessage</c>.
    /// </remarks>
    private async Task PublishAsync<TMessage>(TMessage message) where TMessage : notnull
        => await AsTenantAsync(async () =>
        {
            using var scope = IntakeServices.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);

            return true;
        });

    /// <summary>How many verifications Verify holds for a case, read from Verify's own database.</summary>
    private async Task<long> VerificationsForAsync(Guid caseId)
        => (long)(await ScalarAsync(
            VerifyConnectionString,
            $"""select count(*) from "Verifications" where "CaseId" = '{caseId}'""") ?? 0L);

    /// <summary>A message with no handler anywhere: the control's subject.</summary>
    private sealed record NobodyHandlesThis(Guid CaseId);
}
