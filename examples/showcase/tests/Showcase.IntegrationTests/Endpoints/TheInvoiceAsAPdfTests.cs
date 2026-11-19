using System.Net;
using System.Text;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>GET /api/invoices/{id}/pdf</c>: the invoice from <c>templates/invoice.pdxdoc</c>, in the guest's
///     language.
/// </summary>
public class TheInvoiceAsAPdfTests(PostgresFixture fixture) : InvoiceTestsBase(fixture)
{
    [Fact]
    public async Task AnInvoice_DownloadsAsAPdf()
    {
        var invoiceId = await AnInvoiceAsync();

        var response = await Client.GetAsync($"/api/invoices/{invoiceId}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task AnInvoiceThatDoesNotExist_IsNotFound()
    {
        var response = await Client.GetAsync($"/api/invoices/{Guid.NewGuid()}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
