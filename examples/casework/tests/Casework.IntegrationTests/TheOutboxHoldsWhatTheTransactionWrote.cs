using Casework.Intake;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Messaging.Entities;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The case's change and the event announcing it are one transaction: with delivery
///     stopped, both are in Intake's database.
/// </summary>
/// <remarks>
///     <para>
///         The pump is removed from this host's services, so what is asserted is what the transaction
///         <b>left behind</b> and not what was delivered. That is the whole guarantee: a crash between
///         the commit and the publish cannot lose the event, because there is no gap between them to
///         crash in.
///     </para>
///     <para>
///         Both facts are read with SQL, from the same database, after the operation returned: the
///         outbox row and the column the operation changed. Asserting only the row would be satisfied by
///         an outbox that wrote in a transaction of its own.
///     </para>
/// </remarks>
public sealed class TheOutboxHoldsWhatTheTransactionWrote(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    /// <summary>
    ///     Takes the delivery pump out of Intake's host.
    /// </summary>
    /// <remarks>
    ///     By type and not by a name match: <c>OutboxDeliveryService</c> is the pump, and a test that
    ///     removed "everything with Outbox in the name" would also remove the retention purge and would
    ///     keep working if either were renamed.
    /// </remarks>
    protected override void ConfigureIntake(IServiceCollection services)
    {
        foreach (var pump in services
                     .Where(service => service.ImplementationType == typeof(OutboxDeliveryService))
                     .ToList())
        {
            services.Remove(pump);
        }
    }

    [Fact]
    public async Task WithDeliveryStopped_TheRowAndTheChange_AreBothThere()
    {
        using var scope = IntakeServices.CreateScope();
        var intake = scope.ServiceProvider.GetRequiredService<IIntakeInternalActions>();

        var opened = await AsTenantAsync(() => intake.Cases.OpenCase(
            subject: "A licence for a food stall", applicant: "A. Applicant"));
        opened.IsSuccess.Should().BeTrue(opened.IsSuccess ? "" : $"opening the case was refused: {opened.Error}");

        var asked = await AsTenantAsync(() => intake.Cases.AskForVerification(
            id: opened.Value.Id, kind: "identity"));
        asked.IsSuccess.Should().BeTrue(asked.IsSuccess ? "" : $"asking for a verification was refused: {asked.Error}");

        // The event, written by the interceptor during SaveChanges.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select count(*) from "__OutboxMessages" where "Payload" like '%{opened.Value.Id}%'"""))
            .Should().Be(1L, "the event is a row in the same database, written while the change was saved");

        // And it carries the tenant, which is how the consuming service knows whose row to write. ⚠️ This
        // operation ran inside a TenantScope, which is the path that works: the same publish from an HTTP
        // request leaves this column empty, so the assertion here is deliberately about the
        // ambient path and says so.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select "TenantId" from "__OutboxMessages" where "Payload" like '%{opened.Value.Id}%'"""))
            .Should().Be(Tenant, "the outbox row belongs to the organisation whose change it announces");

        // And the change itself, in the same database. The key's column is "PersistenceId": the entity's
        // Id property is mapped to it, which is what the generated schema calls the row's identity.
        (await ScalarAsync(
                IntakeConnectionString,
                $"""select "VerificationAskedOn" from "Cases" where "PersistenceId" = '{opened.Value.Id}'"""))
            .Should().NotBeNull("the case moved, and it moved in the transaction that wrote the event");
    }

    /// <summary>
    ///     The control: with the pump gone, nothing reaches the other service. It is what makes the test
    ///     above about the <b>transaction</b> rather than about delivery.
    /// </summary>
    [Fact]
    public async Task WithDeliveryStopped_TheOtherServiceSeesNothing()
    {
        using var scope = IntakeServices.CreateScope();
        var intake = scope.ServiceProvider.GetRequiredService<IIntakeInternalActions>();

        var opened = await AsTenantAsync(() => intake.Cases.OpenCase(
            subject: "A licence for a food stall", applicant: "A. Applicant"));
        await AsTenantAsync(() => intake.Cases.AskForVerification(id: opened.Value.Id, kind: "identity"));

        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select count(*) from "Verifications" where "CaseId" = '{opened.Value.Id}'"""))
            .Should().Be(0L, "nothing publishes an outbox row but the pump");
    }
}
