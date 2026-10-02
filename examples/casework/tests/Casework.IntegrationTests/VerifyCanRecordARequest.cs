using Casework.IntegrationTests.Infrastructure;
using Casework.Verify;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The write the message handler makes, made without the message: the operation Verify
///     runs when a request arrives.
/// </summary>
/// <remarks>
///     It exists so that a failure has one meaning. When the broker test goes red, this one says which
///     half is broken: green here and red there is delivery; red here is the write, and the error says
///     why — which a message handler cannot, because a handler's exception is nacked and discarded, not
///     reported to whoever published.
/// </remarks>
public sealed class VerifyCanRecordARequest(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    [Fact]
    public async Task TheOperation_WritesTheRow()
    {
        var caseId = Guid.NewGuid();

        // The boundary's root internal interface, then its group: the same object the handler injects
        // directly. An operation with no [Endpoint] is internal surface, so the module grants this suite
        // InternalsVisibleTo.
        //
        // ⚠️ Inside a tenant, like the handler: the message restores one into the consume scope, and a
        // test that calls the operation directly has to establish it. Without one the write is refused
        // rather than landing in the shared database belonging to nobody.
        var result = await AsTenantAsync(async () =>
        {
            using var scope = VerifyServices.CreateScope();
            var verify = scope.ServiceProvider.GetRequiredService<IVerifyInternalActions>();

            return await verify.Verifications.RecordVerificationRequest(
                caseId: caseId, kind: "identity", deadline: DateTimeOffset.UtcNow.AddDays(10));
        });

        result.IsSuccess.Should().BeTrue(
            result.IsSuccess ? "" : $"the write was refused: {result.Error}");

        (await ScalarAsync(
                VerifyConnectionString,
                $"""select count(*) from "Verifications" where "CaseId" = '{caseId}'"""))
            .Should().Be(1L);
    }
}
