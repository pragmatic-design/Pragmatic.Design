using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Showcase.Billing;
using Showcase.Billing.Actions;
using Showcase.Billing.Entities;
using Showcase.Booking.Dtos;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The invoice the PDF endpoint renders, composed in the guest's language.
/// </summary>
/// <remarks>
///     It needs the invoice with its line items, and the only read of a row with its navigations that every engine serves does not exist
///     yet: EF Core does not implement <c>IIncludingReadRepository</c> (#887). So it reads them through EF Core and runs on EF Core only. The
///     endpoint itself, <see cref="TheInvoiceAsAPdfTests" />, runs on the Sql engine too (#874).
/// </remarks>
public class TheInvoiceInTheGuestsLanguageTests(PostgresFixture fixture) : InvoiceTestsBase(fixture)
{
    /// <summary>
    ///     A guest who reads Italian gets an Italian invoice, with its number and its lines in it.
    /// </summary>
    /// <remarks>
    ///     Asserted on the document model that the endpoint renders — a PDF's text lives in a compressed
    ///     stream, so searching the bytes for a word finds nothing whether or not it is there. The ambient
    ///     culture of the test is not Italian, so the words can only be Italian because the guest is.
    /// </remarks>
    [Fact]
    public async Task TheDocument_IsInTheGuestsLanguage()
    {
        var invoiceId = await AnInvoiceAsync();
        var guest = new GuestDto { FirstName = "Ospite", LastName = "Italiano", PreferredLanguage = "it" };

        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();

        var invoice = await scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BillingBoundary))
            .Set<Invoice>()
            .Include(i => i.LineItems)
            .SingleAsync(i => i.PersistenceId == invoiceId);

        var document = await DownloadInvoicePdfAction.ComposeAsync(
            scope.ServiceProvider.GetRequiredService<IPdxTemplates>(), invoice, guest);

        var words = JsonSerializer.Serialize(document.Model);
        words.Should().Contain("Intestata a").And.Contain("Totale").And.Contain("Ospite Italiano");
        words.Should().Contain(invoice.InvoiceNumber);
        words.Should().NotContain("Billed to");
        document.Warnings.Should().BeEmpty("the endpoint provides every value the template names");
    }
}
