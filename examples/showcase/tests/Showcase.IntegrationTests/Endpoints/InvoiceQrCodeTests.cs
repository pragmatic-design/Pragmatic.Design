using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E test for the Pragmatic.Imaging integration.
///     Exercises the full Rust native pipeline over HTTP — QR encode → decode → resize → PNG encode —
///     via <c>GET /api/invoices/qr.png</c>, asserting a valid, non-empty PNG is returned.
///     This is the Imaging module's Showcase end-to-end coverage (no DB needed; the feature is stateless).
/// </summary>
public class InvoiceQrCodeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InvoiceQr_ReturnsPngImage()
    {
        // Act
        var response = await GetRawAsync("/api/invoices/qr.png?text=INV-2026-0001&size=256");

        // Assert: 200 + PNG content type
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        // Assert: body is a valid, non-empty PNG (magic bytes: 89 50 4E 47 = \x89 P N G)
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'N');
        bytes[3].Should().Be((byte)'G');
    }

    [Fact]
    public async Task InvoiceQr_DefaultsWhenNoText_ReturnsPng()
    {
        var response = await GetRawAsync("/api/invoices/qr.png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
        bytes[0].Should().Be(0x89);
    }
}
