using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Examples.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     Every response type this application's modules answer with through a generated writer is written with the
///     bytes the serializer writes for it under the running host's options.
/// </summary>
/// <remarks>
///     The writers are found, not listed: a response type added to a module is covered by this test the day its
///     writer is generated. The control that the search finds anything is the first assertion.
/// </remarks>
public sealed class EveryResponseWriterWritesTheSerializersBytes(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly System.Reflection.Assembly[] Modules =
    [
        typeof(Invoicing.Registry.Dtos.CustomerDto).Assembly,
        typeof(Invoicing.Billing.Dtos.InvoiceDto).Assembly,
    ];

    [Fact]
    public void EveryGeneratedWriter_WritesTheSerializersBytes()
    {
        var options = Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        ResponseWriterConformance.Used(Modules, options).Should().NotBeEmpty(
            "both modules answer with types the generator writes, and the host's converters claim none of what some of them write");
        ResponseWriterConformance.Mismatches(Modules, options).Should().BeEmpty();
    }
}
