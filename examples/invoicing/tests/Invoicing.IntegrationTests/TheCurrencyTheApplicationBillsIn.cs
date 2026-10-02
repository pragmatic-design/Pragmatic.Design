using System.Net;
using System.Net.Http.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     <c>[SupportedCurrency]</c>: a line priced in a currency this application does not
///     bill in is refused where the value arrives, before anything sums it.
/// </summary>
/// <remarks>
///     <para>
///         The invoice's totals are <c>Money.Zero(Invoice.Euro)</c>, so a line in dollars is not a
///         line worth more or less — it is a line that cannot be added to them. Before this rule the
///         mutation took any currency the caller sent.
///     </para>
///     <para>
///         ⚠️ Not the same rule as <c>CurrencyMismatchError</c>, which lives on the payments path: that
///         one asks whether the money matches <em>this</em> invoice and answers 409 on the loaded row.
///         This one asks whether the application takes the currency at all, and answers 422 before any
///         load — which is also why it is declared on the mutation and not as an invariant.
///     </para>
///     <para>
///         The control is every other test in this suite: they all post <c>currency = "EUR"</c> through
///         the same route and get a draft back. Without that, "a line is refused" would be satisfied by
///         a rule that refuses every line.
///     </para>
/// </remarks>
public sealed class TheCurrencyTheApplicationBillsIn(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task ALineInAnotherCurrency_IsRefused()
    {
        var tenant = await OnboardAsync("curr");
        var accountant = As(TestUsers.Accountant, tenant);
        var customer = await ACustomerAsync(accountant);

        var response = await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId = customer,
            lines = new object[]
            {
                new { description = "Consulting", quantity = 1m, unitPrice = new { amount = 100m, currency = "USD" }, vatRate = 22m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the application bills in euro, and the rule is on the value as it arrives — "
            + await response.Content.ReadAsStringAsync());
    }

    /// <summary>The same body in euro, so the refusal above is about the currency and nothing else.</summary>
    [Fact]
    public async Task TheSameLineInEuro_IsAccepted()
    {
        var tenant = await OnboardAsync("curr-ok");
        var accountant = As(TestUsers.Accountant, tenant);
        var customer = await ACustomerAsync(accountant);

        var response = await accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId = customer,
            lines = new object[]
            {
                new { description = "Consulting", quantity = 1m, unitPrice = new { amount = 100m, currency = "EUR" }, vatRate = 22m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> ACustomerAsync(HttpClient accountant)
    {
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

        return customer.GetProperty("id").GetGuid();
    }
}
