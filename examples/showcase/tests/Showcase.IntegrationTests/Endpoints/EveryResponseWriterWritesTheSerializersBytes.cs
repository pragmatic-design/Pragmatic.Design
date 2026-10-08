using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Examples.Testing;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Every response type the Showcase modules answer with through a generated writer is written with the bytes
///     the serializer writes for it under the running host's options.
/// </summary>
/// <remarks>
///     The Showcase modules opt in to the generated JSON context, so the host's resolver answers from it before
///     reflection: these are the bytes of that path. The writers are found, not listed; the first assertion is the
///     control that any were.
/// </remarks>
public sealed class EveryResponseWriterWritesTheSerializersBytes(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly System.Reflection.Assembly[] Modules =
    [
        typeof(Showcase.Billing.Dtos.InvoiceSummaryDto).Assembly,
        typeof(Showcase.Booking.Dtos.AvailableRoomResult).Assembly,
        typeof(Showcase.Catalog.Amenities.Endpoints.PatchAmenityResult).Assembly,
    ];

    [Fact]
    public void EveryGeneratedWriter_WritesTheSerializersBytes()
    {
        var options = Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        ResponseWriterConformance.Used(Modules, options).Should().NotBeEmpty(
            "the modules answer with types the generator writes, and the host's converters claim none of what some of them write");
        ResponseWriterConformance.Mismatches(Modules, options).Should().BeEmpty();
    }
}
