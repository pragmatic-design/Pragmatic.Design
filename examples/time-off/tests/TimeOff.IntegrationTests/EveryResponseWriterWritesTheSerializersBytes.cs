using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Examples.Testing;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     Every response type the application answers with through a generated writer is written with the bytes the
///     serializer writes for it under the running host's options.
/// </summary>
/// <remarks>The writers are found, not listed; the first assertion is the control that any were.</remarks>
public sealed class EveryResponseWriterWritesTheSerializersBytes(PostgresFixture database) : TimeOffTestBase(database)
{
    private static readonly System.Reflection.Assembly[] Modules = [typeof(TimeOff.Leave.Dtos.EmployeeDto).Assembly];

    [Fact]
    public void EveryGeneratedWriter_WritesTheSerializersBytes()
    {
        var options = Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        ResponseWriterConformance.Used(Modules, options).Should().NotBeEmpty(
            "the module answers with types the generator writes, and the host's converters claim none of what some of them write");
        ResponseWriterConformance.Mismatches(Modules, options).Should().BeEmpty();
    }
}
