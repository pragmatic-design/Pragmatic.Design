using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     What the customer paid: partial payments add up, the invoice becomes paid when they
///     reach the total, and more than is owed is refused.
/// </summary>
/// <remarks>
///     The invoice of every test here is 2 × 250,00 at 22% — 500,00 net, 110,00 VAT, <b>610,00</b> gross.
///     The numbers below are that invoice's, written out rather than computed, so a change to the rounding
///     rule fails here too.
/// </remarks>
public sealed class RecordingPayments(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task APartialPayment_LeavesTheInvoiceIssued_AndShowsWhatIsDue()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        var after = await company.PayAsync(invoice, 200m);

        after.GetProperty("status").GetString().Should().Be("Issued", "part of it is not all of it");
        after.GetProperty("amountPaid").GetProperty("amount").GetDecimal().Should().Be(200m);
        after.GetProperty("amountDue").GetProperty("amount").GetDecimal().Should().Be(410m);
    }

    [Fact]
    public async Task PaymentsThatReachTheTotal_MakeTheInvoicePaid()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        await company.PayAsync(invoice, 200m);
        var after = await company.PayAsync(invoice, 410m);

        after.GetProperty("status").GetString().Should().Be("Paid");
        after.GetProperty("amountDue").GetProperty("amount").GetDecimal().Should().Be(0m);
    }

    /// <summary>
    ///     The refusal names what is still owed: a client that got it wrong can say so to whoever typed it.
    /// </summary>
    [Fact]
    public async Task MoreThanIsOwed_IsRefusedWith422_NamingTheAmountDue()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();
        await company.PayAsync(invoice, 200m);

        var response = await company.TryPayAsync(invoice, 500m);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.GetProperty("amountDue").GetDecimal().Should().Be(410m);
    }

    [Fact]
    public async Task APaymentOnADraft_IsRefusedWith409()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();

        var response = await company.TryPayAsync(draft, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "nobody was asked to pay it yet");
    }

    /// <summary>
    ///     The control on the other end: nothing is not an amount, and minus fifty would <em>lower</em>
    ///     what has been paid — a refund written as a payment, which this application does not have.
    /// </summary>
    [Fact]
    public async Task APaymentOfNothingOrLess_IsRefused()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        var zero = await company.TryPayAsync(invoice, 0m);
        var negative = await company.TryPayAsync(invoice, -50m);

        zero.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        negative.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task APaymentInAnotherCurrency_IsRefused()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        var response = await company.TryPayAsync(invoice, 100m, currency: "USD");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an invoice is settled in the currency it was issued in — refused, not added to the euros");
    }

    [Fact]
    public async Task DeletingAPayment_PutsThePaidInvoiceBackToIssued()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();
        var paid = await company.PayAsync(invoice, 610m);
        paid.GetProperty("status").GetString().Should().Be("Paid");

        var payment = (await company.PaymentsOfAsync(invoice))[0].GetProperty("id").GetGuid();
        var after = await company.DeletePaymentAsync(invoice, payment);

        after.GetProperty("status").GetString().Should().Be("Issued", "it is owed again");
        after.GetProperty("amountPaid").GetProperty("amount").GetDecimal().Should().Be(0m);
        after.GetProperty("amountDue").GetProperty("amount").GetDecimal().Should().Be(610m);
    }

    /// <summary>
    ///     The control. <c>AmountPaid</c> is kept on the invoice and is therefore a second copy of what the
    ///     payments say; this asserts the two never disagree along a sequence that writes both — two
    ///     recorded, one removed — and that the list is the one the total is made of.
    /// </summary>
    [Fact]
    public async Task TheAmountPaidNeverDisagreesWithThePayments()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        await company.PayAsync(invoice, 100m);
        await company.PayAsync(invoice, 200m);

        var recorded = await company.PaymentsOfAsync(invoice);
        var amounts = recorded.Select(p => p.GetProperty("amount").GetProperty("amount").GetDecimal()).ToList();
        amounts.Should().BeEquivalentTo(new[] { 100m, 200m },
            "the list carries the amounts — a projection that drops them answers zero and reads as a "
            + "payment of nothing (fixed here in SqlTranslatableAnalyzer)");

        var after = await company.DeletePaymentAsync(invoice, recorded[amounts.IndexOf(100m)].GetProperty("id").GetGuid());
        var remaining = await company.PaymentsOfAsync(invoice);

        remaining.Should().ContainSingle();
        after.GetProperty("amountPaid").GetProperty("amount").GetDecimal()
            .Should().Be(remaining.Sum(p => p.GetProperty("amount").GetProperty("amount").GetDecimal()));
        after.GetProperty("amountDue").GetProperty("amount").GetDecimal().Should().Be(410m);
    }

    [Fact]
    public async Task APaymentOnAnotherTenantsInvoice_IsNotFound()
    {
        var acme = await ACompanyAsync("acme");
        var globex = await ACompanyAsync("globex");
        var theirs = await acme.IssuedInvoiceAsync();

        var response = await globex.TryPayAsync(theirs, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        return new Company(accountant, customer.GetProperty("id").GetGuid());
    }

    private sealed record Company(HttpClient Accountant, Guid CustomerId)
    {
        /// <summary>A draft of 610,00 gross, and its id.</summary>
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

        public async Task<Guid> IssuedInvoiceAsync()
        {
            var draft = await DraftAsync();
            await ReadSuccessAsync(await Accountant.PostAsync($"api/invoices/{draft}/issue", null));

            return draft;
        }

        public async Task<JsonElement> PayAsync(Guid invoice, decimal amount)
            => await ReadJsonAsync(await TryPayAsync(invoice, amount));

        public Task<HttpResponseMessage> TryPayAsync(Guid invoice, decimal amount, string currency = "EUR")
            => Accountant.PostAsJsonAsync($"api/invoices/{invoice}/payments", new
            {
                paidOn = "2026-04-02",
                amount = new { amount, currency },
                reference = "BONIFICO 4471",
                method = "BankTransfer",
            });

        public async Task<JsonElement> DeletePaymentAsync(Guid invoice, Guid payment)
            => await ReadJsonAsync(await Accountant.DeleteAsync($"api/invoices/{invoice}/payments/{payment}"));

        public async Task<IReadOnlyList<JsonElement>> PaymentsOfAsync(Guid invoice)
        {
            var body = await ReadJsonAsync(await Accountant.GetAsync($"api/invoices/{invoice}/payments"));

            return [.. body.EnumerateArray()];
        }
    }
}
