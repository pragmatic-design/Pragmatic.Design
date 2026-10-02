using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     A draft invoice: lines written through their invoice, money rounded per line, and a
///     customer that has to exist in this company.
/// </summary>
public sealed class DraftingAnInvoice(PostgresFixture database) : InvoicingTestBase(database)
{
    /// <summary>
    ///     The numbers are written here rather than recomputed by the test: 3 × 19.99 at 22% is 59.97 and
    ///     13.19 (59.97 × 0.22 = 13.1934), 1 × 100.00 at 10% is 100.00 and 10.00.
    /// </summary>
    [Fact]
    public async Task ADraftWithTwoLines_ComputesItsTotals()
    {
        var (accountant, customerId) = await ACompanyWithACustomerAsync();

        var invoice = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = new object[]
            {
                Line("Consulting", quantity: 3, unitPrice: 19.99m, vatRate: 22m),
                Line("Licence", quantity: 1, unitPrice: 100.00m, vatRate: 10m),
            },
        }));

        Amount(invoice, "netTotal").Should().Be(159.97m);
        Amount(invoice, "vatTotal").Should().Be(23.19m);
        Amount(invoice, "grossTotal").Should().Be(183.16m);
    }

    /// <summary>
    ///     The control for the rule above: each line is rounded and then they are added. Two lines of
    ///     2.5 × 0.05 are 0.125 each — 0.12 rounded to even — so the invoice is 0.24. A sum of the
    ///     unrounded products would be 0.25, and the total on the paper would not be the sum of the lines
    ///     printed above it.
    /// </summary>
    [Fact]
    public async Task RoundingIsPerLine_NotOnTheSum()
    {
        var (accountant, customerId) = await ACompanyWithACustomerAsync();

        var invoice = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = new object[]
            {
                Line("Half a cent", quantity: 2.5m, unitPrice: 0.05m, vatRate: 0m),
                Line("Half a cent again", quantity: 2.5m, unitPrice: 0.05m, vatRate: 0m),
            },
        }));

        Amount(invoice, "netTotal").Should().Be(0.24m);
    }

    [Fact]
    public async Task AnUnknownVatRate_IsRefused()
    {
        var (accountant, customerId) = await ACompanyWithACustomerAsync();

        var response = await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = new object[] { Line("At a rate nobody charges", quantity: 1, unitPrice: 10m, vatRate: 13m) },
        });

        // The body in the message: a test that fails saying only "400" has to be run again to learn why.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADraftWithNoLines_IsRefused()
    {
        var (accountant, customerId) = await ACompanyWithACustomerAsync();

        var response = await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an invoice with no lines is not a document, and the invariant says so before the row lands");
    }

    /// <summary>
    ///     404 and not 403: the customer of another company is as absent as one that never existed, which
    ///     is the answer the tenant filter gives — and this is also the assertion that the cross-module
    ///     read is filtered too.
    /// </summary>
    [Fact]
    public async Task AnInvoiceForACustomerOfAnotherTenant_IsNotFound()
    {
        var (_, theirCustomer) = await ACompanyWithACustomerAsync("globex");
        var (ours, _) = await ACompanyWithACustomerAsync("acme");

        var response = await ours.PostAsJsonAsync("api/invoices", new
        {
            customerId = theirCustomer,
            lines = new object[] { Line("For somebody else's customer", quantity: 1, unitPrice: 10m, vatRate: 22m) },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EditingADraft_ReplacesItsLines()
    {
        var (accountant, customerId) = await ACompanyWithACustomerAsync();
        var invoice = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId,
            lines = new object[]
            {
                Line("Kept", quantity: 1, unitPrice: 10m, vatRate: 22m),
                Line("Removed", quantity: 1, unitPrice: 50m, vatRate: 22m),
            },
        }));

        var kept = invoice.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("description").GetString() == "Kept");

        var edited = await ReadJsonAsync(await accountant.PutAsJsonAsync(
            $"api/invoices/{invoice.GetProperty("id").GetGuid()}", new
            {
                id = invoice.GetProperty("id").GetGuid(),
                lines = new object[]
                {
                    // The same line, with a changed quantity: its id is what keeps its identity.
                    new { id = kept.GetProperty("id").GetGuid(), description = "Kept", quantity = 2m, unitPrice = Euro(10m), vatRate = 22m },
                    Line("Added", quantity: 1, unitPrice: 5m, vatRate: 22m),
                },
            }));

        var descriptions = edited.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("description").GetString()).ToList();

        descriptions.Should().BeEquivalentTo(["Kept", "Added"]);
        Amount(edited, "netTotal").Should().Be(25.00m, "2 × 10 plus 1 × 5");
    }

    private async Task<(HttpClient Accountant, Guid CustomerId)> ACompanyWithACustomerAsync(string company = "acme")
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync(company));
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

        return (accountant, customer.GetProperty("id").GetGuid());
    }

    private static object Line(string description, decimal quantity, decimal unitPrice, decimal vatRate) =>
        new { description, quantity, unitPrice = Euro(unitPrice), vatRate };

    private static object Euro(decimal amount) => new { amount, currency = "EUR" };

    private static decimal Amount(JsonElement invoice, string total) =>
        invoice.GetProperty(total).GetProperty("amount").GetDecimal();
}
