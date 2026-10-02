using Casework.Intake;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     And then the pump publishes it: the operation's event reaches the other service
///     without anybody publishing it by hand.
/// </summary>
/// <remarks>
///     <para>
///         The difference from <c>AMessageCrossesTheBroker</c> is the whole point: there, the suite calls
///         <c>IMessageBus.PublishAsync</c> itself, outside any transaction; here an <b>operation</b>
///         changes a row, the entity raises the event, the outbox writes it in the same transaction and
///         the pump delivers it afterwards. Nothing in the application names the bus.
///     </para>
///     <para>
///         ⚠️ What makes this test pass is <b>generated</b>: the generator emits an
///         <c>IMessageTypeRegistry</c> for every assembly that declares domain events, and the host names
///         the contracts assembly's registration in one line. Without it a publisher's own event is an
///         unknown message type to its own pump and every row is dead-lettered. This is the test that
///         goes red if that line is removed.
///     </para>
/// </remarks>
public sealed class TheOutboxDeliversWhatItHeld(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    [Fact]
    public async Task TheOperationsEvent_ReachesTheOtherService()
    {
        await WaitForSubscriberAsync("verification-requested");

        using var scope = IntakeServices.CreateScope();
        var intake = scope.ServiceProvider.GetRequiredService<IIntakeInternalActions>();

        var opened = await AsTenantAsync(() => intake.Cases.OpenCase(
            subject: "A permit for a market stall", applicant: "B. Applicant"));
        opened.IsSuccess.Should().BeTrue(opened.IsSuccess ? "" : $"opening the case was refused: {opened.Error}");

        var asked = await AsTenantAsync(() => intake.Cases.AskForVerification(
            id: opened.Value.Id, kind: "address"));
        asked.IsSuccess.Should().BeTrue(asked.IsSuccess ? "" : $"asking for a verification was refused: {asked.Error}");

        await EventuallyAsync(
            async () => await VerificationsForAsync(opened.Value.Id) == 1,
            $"Verify recorded the verification of case {opened.Value.Id} — the outbox never delivered it");

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select "Kind" from "Verifications" where "CaseId" = '{opened.Value.Id}'"""))
            .Should().Be("address", "what travelled is the event the entity raised, with its own data");
    }

    private async Task<long> VerificationsForAsync(Guid caseId)
        => (long)(await ScalarAsync(
            VerifyConnectionString,
            $"""select count(*) from "Verifications" where "CaseId" = '{caseId}'""") ?? 0L);
}
