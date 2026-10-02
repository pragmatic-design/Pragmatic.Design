using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Invoicing.Billing.Entities;
using Invoicing.Billing.Invoices;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Organizations.Queries;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The invoice as a document: rendered once at issue, stored under the company's own
///     container, and served as the same bytes for ever.
/// </summary>
public sealed class TheInvoiceAsAPdf(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task AnIssuedInvoice_HasAPdf()
    {
        var (accountant, invoice) = await AnIssuedInvoiceAsync();

        var response = await accountant.GetAsync($"api/invoices/{invoice.GetProperty("id").GetGuid()}/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        bytes.Length.Should().Be((int)invoice.GetProperty("pdfByteLength").GetInt64(),
            "the file served is the one the issue recorded");
    }

    /// <summary>
    ///     A document does not change. Two downloads are the same bytes, and their hash is the one recorded
    ///     the day the invoice was issued — which is what tells a stored file from a fresh rendering.
    /// </summary>
    [Fact]
    public async Task TheSameInvoice_DownloadsTheSameBytesTwice()
    {
        var (accountant, invoice) = await AnIssuedInvoiceAsync();
        var route = $"api/invoices/{invoice.GetProperty("id").GetGuid()}/pdf";

        var first = await (await accountant.GetAsync(route)).Content.ReadAsByteArrayAsync();
        var second = await (await accountant.GetAsync(route)).Content.ReadAsByteArrayAsync();

        var hash = Convert.ToHexString(SHA256.HashData(first));
        hash.Should().Be(Convert.ToHexString(SHA256.HashData(second)));
        hash.Should().Be(invoice.GetProperty("pdfSha256").GetString());
    }

    [Fact]
    public async Task ADraft_HasNoPdf()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();

        var response = await company.Accountant.GetAsync($"api/invoices/{draft}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a draft is not a document yet");
    }

    [Fact]
    public async Task AnInvoiceOfAnotherCompany_IsNotFound()
    {
        var (_, theirs) = await AnIssuedInvoiceAsync("acme");
        var globex = await ACompanyAsync("globex");

        var response = await globex.Accountant.GetAsync($"api/invoices/{theirs.GetProperty("id").GetGuid()}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     The document carries the customer as they were at issue — the whole point of freezing them.
    /// </summary>
    /// <remarks>
    ///     Asserted on the model the renderer is given rather than on the rendered bytes: a PDF's text
    ///     lives in a compressed content stream, so searching the file for a name finds nothing whether or
    ///     not the name is in it — the assertion would pass for the wrong reason the day it stopped being
    ///     true. The model is built here by the same call the issue action makes, from the invoice as it
    ///     was stored.
    /// </remarks>
    [Fact]
    public async Task TheDocumentCarriesTheFrozenCustomer_NotTheCustomer()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();
        var issued = await ReadJsonAsync(await company.Accountant.PostAsync($"api/invoices/{draft}/issue", null));

        await ReadSuccessAsync(await company.Accountant.PutAsJsonAsync($"api/customers/{company.CustomerId}", new
        {
            id = company.CustomerId,
            name = "Renamed S.p.A.",
        }));

        var model = await ModelOfAsync(company.Tenant, issued.GetProperty("id").GetGuid());
        var text = JsonSerializer.Serialize(model);

        text.Should().Contain("A customer", "the document is built from the invoice's frozen copy");
        text.Should().NotContain("Renamed S.p.A.", "and never from the customer as they are now");
    }

    /// <summary>The document model of a stored invoice, built the way the issue action builds it.</summary>
    private async Task<Pragmatic.Documents.Model.DocumentModel> ModelOfAsync(string tenant, Guid invoiceId)
    {
        using var tenantScope = TenantScope.BeginScope(tenant);
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var invoice = await scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>()
            .GetByIdAsync(invoiceId);
        var issuer = await scope.ServiceProvider.GetRequiredService<IRegistryReads>()
            .GetOrganizationBillingDetails(new GetOrganizationBillingDetailsQuery { Slug = tenant });

        return await InvoiceDocument.ForTheCustomerAsync(
            scope.ServiceProvider.GetRequiredService<IPdxTemplates>(), invoice!, issuer[0]);
    }

    private async Task<(HttpClient Accountant, JsonElement Invoice)> AnIssuedInvoiceAsync(string name = "acme")
    {
        var company = await ACompanyAsync(name);
        var draft = await company.DraftAsync();
        var issued = await ReadJsonAsync(await company.Accountant.PostAsync($"api/invoices/{draft}/issue", null));

        return (company.Accountant, issued);
    }

    private async Task<Company> ACompanyAsync(string name = "acme")
    {
        var tenant = await OnboardAsync(name);
        var accountant = As(TestUsers.Accountant, tenant);
        var customer = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", new
        {
            name = "A customer",
            vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email = "billing@customer.test",
            preferredCulture = "it-IT",
            paymentTermsDays = 30,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        }));

        return new Company(accountant, customer.GetProperty("id").GetGuid(), tenant);
    }

    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Tenant)
    {
        public async Task<Guid> DraftAsync()
        {
            var draft = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/invoices", new
            {
                customerId = CustomerId,
                lines = new object[]
                {
                    new { description = "Consulting", quantity = 2m, unitPrice = new { amount = 250m, currency = "EUR" }, vatRate = 22m },
                },
            }));

            return draft.GetProperty("id").GetGuid();
        }
    }
}
