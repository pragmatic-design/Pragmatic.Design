using Casework.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Examples.Testing;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     Every response type the two services answer with through a generated writer is written with the bytes the
///     serializer writes for it under that service's own running host options.
/// </summary>
/// <remarks>The writers are found, not listed; the first assertion of each is the control that any were.</remarks>
public sealed class EveryResponseWriterWritesTheSerializersBytes(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    [Fact]
    public void Intake_EveryGeneratedWriter_WritesTheSerializersBytes()
        => Conforms(IntakeServices, typeof(Casework.Intake.Dtos.CaseDocumentDto).Assembly);

    [Fact]
    public void Verify_EveryGeneratedWriter_WritesTheSerializersBytes()
        => Conforms(VerifyServices, typeof(Casework.Verify.Dtos.VerificationDto).Assembly);

    private static void Conforms(IServiceProvider host, System.Reflection.Assembly module)
    {
        var options = host.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        ResponseWriterConformance.Used([module], options).Should().NotBeEmpty(
            "the module answers with types the generator writes, and the host's converters claim none of what some of them write");
        ResponseWriterConformance.Mismatches([module], options).Should().BeEmpty();
    }
}
