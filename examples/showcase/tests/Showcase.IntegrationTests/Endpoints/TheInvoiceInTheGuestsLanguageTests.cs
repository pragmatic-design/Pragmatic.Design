using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Actions;
using Showcase.Billing.Entities;
using Showcase.Booking.Dtos;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The invoice the PDF endpoint renders, composed in the guest's language.
/// </summary>
/// <remarks>
///     It needs the invoice with its line items, read through <c>IIncludingReadRepository</c>, which EF Core's repository and the Sql
///     engine's both implement (#887). On the documents engine the action it composes with is left out, as the endpoint is
///     (<see cref="TheInvoiceAsAPdfTests" />): a document keeps a relation as its key, with no navigation to load.
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
        // Outside a request no caller holds a scope on the invoice ([HasAccessScopes]): the row filters are lifted, the subject is the
        // document. That the read stays inside them is ARowReadWithItsNavigationsStaysInsideTheFiltersTests.
        using var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>().DisableAll();

        // The repository is registered as IReadRepository; the including read is the same instance, as the generated loads cast it.
        var repository = (IIncludingReadRepository<Invoice>)scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>();
        var invoice = await repository.GetByIdIncludingAsync(invoiceId, nameof(Invoice.LineItems));
        invoice.Should().NotBeNull("the invoice was created in this tenant");
        invoice!.LineItems.Should().NotBeEmpty("the read loads the navigation it names");

        var document = await DownloadInvoicePdfAction.ComposeAsync(
            scope.ServiceProvider.GetRequiredService<IPdxTemplates>(), invoice, guest);

        var words = JsonSerializer.Serialize(document.Model);
        words.Should().Contain("Intestata a").And.Contain("Totale").And.Contain("Ospite Italiano");
        words.Should().Contain(invoice.InvoiceNumber);
        words.Should().NotContain("Billed to");
        document.Warnings.Should().BeEmpty("the endpoint provides every value the template names");
    }
}
