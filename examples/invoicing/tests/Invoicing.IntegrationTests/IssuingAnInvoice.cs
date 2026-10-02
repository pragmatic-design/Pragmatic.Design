using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     Issuing: the company's next number for the year, and the customer frozen onto the
///     document.
/// </summary>
/// <remarks>
///     The clock is sealed, because an invoice number carries a year and a test that reads the machine's
///     would pass every day but one.
/// </remarks>
public sealed class IssuingAnInvoice(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly DateTimeOffset Pinned = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(new TestClock(Pinned));

    [Fact]
    public async Task TheFirstInvoiceOfTheYear_IsNumberOne()
    {
        var company = await ACompanyAsync();

        var issued = await company.IssueAsync(await company.DraftAsync());

        issued.GetProperty("number").GetString().Should().Be($"{company.Prefix}/2026/0001");
        issued.GetProperty("status").GetString().Should().Be("Issued");
        issued.GetProperty("issuedOn").GetString().Should().Be("2026-03-14");
        issued.GetProperty("dueOn").GetString().Should().Be("2026-04-13", "thirty days, as the customer's terms say");
    }

    [Fact]
    public async Task TwentyInvoices_AreNumberedWithoutHoles()
    {
        var company = await ACompanyAsync();

        var numbers = new List<string?>();
        for (var i = 0; i < 20; i++)
            numbers.Add((await company.IssueAsync(await company.DraftAsync())).GetProperty("number").GetString());

        numbers.Should().BeEquivalentTo(
            Enumerable.Range(1, 20).Select(n => $"{company.Prefix}/2026/{n:D4}"),
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task TwoCompanies_NumberTheirOwnInvoices()
    {
        var acme = await ACompanyAsync("acme");
        var globex = await ACompanyAsync("globex");

        var first = await acme.IssueAsync(await acme.DraftAsync());
        var second = await globex.IssueAsync(await globex.DraftAsync());

        first.GetProperty("number").GetString().Should().Be($"{acme.Prefix}/2026/0001");
        second.GetProperty("number").GetString().Should().Be($"{globex.Prefix}/2026/0001",
            "a database sequence would have given this one 0002");
    }

    /// <summary>
    ///     Five issues at once: one of them may lose the concurrency check on the series row and come back
    ///     409, which the caller retries — and the five numbers that result are exactly 0001 to 0005, with
    ///     none reused and none skipped.
    /// </summary>
    [Fact]
    public async Task FiveConcurrentIssues_ProduceFiveConsecutiveNumbers()
    {
        var company = await ACompanyAsync();
        var drafts = new List<Guid>();
        for (var i = 0; i < 5; i++)
            drafts.Add(await company.DraftAsync());

        var numbers = await Task.WhenAll(drafts.Select(company.IssueRetryingAsync));

        numbers.Should().BeEquivalentTo(Enumerable.Range(1, 5).Select(n => $"{company.Prefix}/2026/{n:D4}"));
    }

    /// <summary>
    ///     The reason this story exists: the document keeps the customer it was issued to, whatever happens
    ///     to the customer afterwards.
    /// </summary>
    [Fact]
    public async Task TheCustomerSnapshot_DoesNotFollowTheCustomer()
    {
        var company = await ACompanyAsync();
        var issued = await company.IssueAsync(await company.DraftAsync());

        await ReadSuccessAsync(await company.Accountant.PutAsJsonAsync($"api/customers/{company.CustomerId}", new
        {
            id = company.CustomerId,
            name = "Renamed S.p.A.",
            addressCity = "Torino",
        }));

        var reissued = await ReadJsonAsync(await company.Accountant.GetAsync($"api/customers/{company.CustomerId}"));

        reissued.GetProperty("name").GetString().Should().Be("Renamed S.p.A.", "the customer did change");
        issued.GetProperty("billedToName").GetString().Should().Be("A customer",
            "and the invoice still says what it said the day it was issued");
    }

    [Fact]
    public async Task IssuingTwice_IsRefusedWith409()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();
        await company.IssueAsync(draft);

        var again = await company.Accountant.PostAsync($"api/invoices/{draft}/issue", null);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the state refuses it, with a code, before a number is taken");
    }

    [Fact]
    public async Task ChangingAnIssuedInvoice_IsRefusedWith409()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();
        await company.IssueAsync(draft);

        var response = await company.Accountant.PutAsJsonAsync($"api/invoices/{draft}", new
        {
            id = draft,
            lines = new object[]
            {
                new { description = "Sneaked in", quantity = 1m, unitPrice = new { amount = 1m, currency = "EUR" }, vatRate = 22m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task IssuingAnInvoiceOfAnotherCompany_IsNotFound()
    {
        var acme = await ACompanyAsync("acme");
        var globex = await ACompanyAsync("globex");
        var theirDraft = await acme.DraftAsync();

        var response = await globex.Accountant.PostAsync($"api/invoices/{theirDraft}/issue", null);

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

        return new Company(accountant, customer.GetProperty("id").GetGuid(), name.ToUpperInvariant());
    }

    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Prefix)
    {
        /// <summary>A draft with one line, and its id.</summary>
        public async Task<Guid> DraftAsync()
        {
            var draft = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/invoices", new
            {
                customerId = CustomerId,
                lines = new object[]
                {
                    new { description = "Consulting", quantity = 1m, unitPrice = new { amount = 100m, currency = "EUR" }, vatRate = 22m },
                },
            }));

            return draft.GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> IssueAsync(Guid invoice)
            => await ReadJsonAsync(await Accountant.PostAsync($"api/invoices/{invoice}/issue", null));

        /// <summary>Issues, retrying while the number is contended — which is what a client does with a 409.</summary>
        public async Task<string?> IssueRetryingAsync(Guid invoice)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var response = await Accountant.PostAsync($"api/invoices/{invoice}/issue", null);
                if (response.StatusCode != HttpStatusCode.Conflict)
                    return (await ReadJsonAsync(response)).GetProperty("number").GetString();
            }

            throw new Xunit.Sdk.XunitException($"{invoice} was contended ten times running");
        }
    }
}
