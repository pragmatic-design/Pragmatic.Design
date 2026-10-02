using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     Finding invoices: filters the database evaluates, and what each customer still owes.
/// </summary>
/// <remarks>
///     The clock is the test's, because "late" is a question about a day: invoices are issued on the 14th
///     of March 2026 and fall due thirty days later, and each test says which day it is asking about.
/// </remarks>
public sealed class ReadingInvoices(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Issued);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(_clock);

    /// <summary>
    ///     Twenty invoices issued on the same day: the order is then the number, descending, and page two
    ///     of five is the fifteenth down to the eleventh.
    /// </summary>
    [Fact]
    public async Task TheListIsPagedAndSorted()
    {
        var company = await ACompanyAsync();
        for (var i = 0; i < 20; i++)
            await company.IssueAsync();

        var page = await company.ListAsync("page=2&pageSize=5");

        NumbersOf(page).Should().Equal(
            $"{company.Prefix}/2026/0015",
            $"{company.Prefix}/2026/0014",
            $"{company.Prefix}/2026/0013",
            $"{company.Prefix}/2026/0012",
            $"{company.Prefix}/2026/0011");
        page.GetProperty("totalCount").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task EachFilterNarrowsTheList()
    {
        var company = await ACompanyAsync();
        var other = await company.AnotherCustomerAsync();
        var issued = await company.IssueAsync();
        var draft = await company.DraftAsync();
        var theirs = await company.IssueAsync(other);

        NumbersOf(await company.ListAsync("status=Draft")).Should().Equal([null], "a draft has no number yet");
        IdsOf(await company.ListAsync("status=Draft")).Should().Equal(draft);
        IdsOf(await company.ListAsync("status=Issued")).Should().BeEquivalentTo(new[] { issued, theirs });
        IdsOf(await company.ListAsync($"customerId={other}")).Should().Equal(theirs);
        IdsOf(await company.ListAsync("issuedFrom=2026-03-14&issuedTo=2026-03-14"))
            .Should().BeEquivalentTo(new[] { issued, theirs }, "the day they were issued");
        IdsOf(await company.ListAsync("issuedFrom=2026-03-15")).Should().BeEmpty("nothing was issued later");

        IdsOf(await company.ListAsync($"status=Issued&customerId={other}&issuedTo=2026-03-14"))
            .Should().Equal([theirs], "the filters narrow together");
    }

    /// <summary>
    ///     One box over the number and the frozen name — and in either case, which is what
    ///     <c>IgnoreCase</c> buys: PostgreSQL compares as written.
    /// </summary>
    [Fact]
    public async Task TheSearchBoxFindsANumberAndAName_WhateverTheCase()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();

        IdsOf(await company.ListAsync($"search={company.Prefix.ToLowerInvariant()}/2026/0001"))
            .Should().Equal([invoice], "the number, typed in lower case");
        IdsOf(await company.ListAsync("search=CUSTOMER")).Should().Equal([invoice], "the name, typed in upper case");
        IdsOf(await company.ListAsync("search=nobody")).Should().BeEmpty("the control: it does not match everything");
    }

    [Fact]
    public async Task OnlyOverdueInvoicesAreReturned_ForTheDayTheClockSays()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();

        // The day before it falls due: nothing is late, and the filter is what says so.
        _clock.SetDate(2026, 4, 13);
        IdsOf(await company.ListAsync("overdue=true")).Should().BeEmpty("due today is not late");
        IdsOf(await company.ListAsync("overdue=false")).Should().Equal(invoice);

        _clock.SetDate(2026, 4, 14);
        IdsOf(await company.ListAsync("overdue=true")).Should().Equal([invoice], "one day past due");
    }

    /// <summary>
    ///     A settled invoice is not late, whatever the date: the rule is the entity's own, and the list and
    ///     the reminder sweep read the same one.
    /// </summary>
    [Fact]
    public async Task APaidInvoiceIsNeverOverdue()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();
        await company.PayAsync(invoice, 610m);

        _clock.SetDate(2026, 6, 1);

        IdsOf(await company.ListAsync("overdue=true")).Should().BeEmpty();
    }

    /// <summary>
    ///     The aggregate the database computes equals what the test can add up from what it created.
    /// </summary>
    [Fact]
    public async Task TheOutstandingViewAgreesWithTheInvoices()
    {
        var company = await ACompanyAsync();
        var other = await company.AnotherCustomerAsync();
        var first = await company.IssueAsync();
        await company.IssueAsync();
        var theirs = await company.IssueAsync(other);
        await company.PayAsync(first, 100m);
        await company.PayAsync(theirs, 610m);
        await company.DraftAsync();

        Sql.Clear();
        var page = await company.OutstandingAsync();
        var lines = page.GetProperty("items").EnumerateArray().ToList();

        // The aggregation is the database's, which is the whole point of a [QueryView]: without this the
        // numbers below would be just as right with every invoice loaded and added up in C#.
        Sql.Commands.Should().Contain(sql => sql.Contains("GROUP BY", StringComparison.Ordinal)
                                             && sql.Contains("sum(", StringComparison.OrdinalIgnoreCase));

        lines.Should().ContainSingle("the settled customer owes nothing, and a draft is not owed at all");
        var line = lines[0];
        line.GetProperty("customerId").GetGuid().Should().Be(company.CustomerId);
        line.GetProperty("invoices").GetInt32().Should().Be(2);
        line.GetProperty("outstanding").GetDecimal().Should().Be(1120m, "610,00 + 610,00 less the 100,00 paid");
        line.GetProperty("oldestDueOn").GetString().Should().Be("2026-04-13");
    }

    /// <summary>
    ///     The same aggregate over only what is late — an aggregation over a set narrowed by the
    ///     <c>[ComputedFilter]</c> method, which is what the story left as an open question.
    /// </summary>
    [Fact]
    public async Task TheOutstandingViewCanBeNarrowedToWhatIsLate()
    {
        var company = await ACompanyAsync();
        await company.IssueAsync();

        _clock.SetDate(2026, 4, 13);
        var onTime = await company.OutstandingAsync("overdue=true");

        _clock.SetDate(2026, 4, 14);
        var late = await company.OutstandingAsync("overdue=true");

        onTime.GetProperty("items").EnumerateArray().Should().BeEmpty("due today is not late");
        CustomerIdsOf(late).Should().Equal([company.CustomerId]);
        late.GetProperty("items").EnumerateArray().Single()
            .GetProperty("outstanding").GetDecimal().Should().Be(610m);
    }

    /// <summary>
    ///     What the page costs. The assertion is that the number of statements does not move with the
    ///     number of rows — which is what an N+1 does, and the only thing a magic number would not catch:
    ///     a paged read is a count and a page, and both are the read this query declares.
    /// </summary>
    [Fact]
    public async Task ThePageCostsTheSameNumberOfStatements_WhateverThePageSize()
    {
        var company = await ACompanyAsync();
        for (var i = 0; i < 10; i++)
            await company.IssueAsync();

        Sql.Clear();
        await company.ListAsync("page=1&pageSize=2");
        var forTwo = ReadsOfInvoices();

        Sql.Clear();
        await company.ListAsync("page=1&pageSize=10");
        var forTen = ReadsOfInvoices();

        forTen.Should().Be(forTwo, "a page of ten costs what a page of two costs");
        forTen.Should().Be(2, "the count and the page, and nothing per row");
    }

    [Fact]
    public async Task TheListShowsOnlyThisTenantsInvoices()
    {
        var acme = await ACompanyAsync("acme");
        var globex = await ACompanyAsync("globex");
        var theirs = await acme.IssueAsync();
        var ours = await globex.IssueAsync();

        IdsOf(await globex.ListAsync("")).Should().Equal(ours);
        IdsOf(await acme.ListAsync("")).Should().Equal(theirs);
        CustomerIdsOf(await globex.OutstandingAsync()).Should().Equal([globex.CustomerId],
            "the aggregate is over this company's invoices too, not over everybody's");
    }

    private static IEnumerable<Guid> IdsOf(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());

    /// <summary>
    ///     The numbers on the page, with a draft's missing one as null.
    /// </summary>
    /// <remarks>
    ///     <c>TryGetProperty</c>, not <c>GetProperty</c>: a null member is <b>absent</b> from the response —
    ///     the application's JSON options omit nulls — and asking for it throws
    ///     <c>KeyNotFoundException</c> rather than answering null. Worth knowing before writing a client.
    /// </remarks>
    private static IEnumerable<string?> NumbersOf(JsonElement page)
        => page.GetProperty("items").EnumerateArray()
            .Select(i => i.TryGetProperty("number", out var number) ? number.GetString() : null);

    /// <summary>
    ///     How many of the captured statements read the invoices.
    /// </summary>
    /// <remarks>
    ///     Counted by table rather than in total: every request also reads <c>Organizations</c>, because
    ///     the tenant middleware checks that the company in the token exists and is served. That is one
    ///     read per request and not per row, which is a different question from this one.
    /// </remarks>
    private int ReadsOfInvoices()
        => Sql.Commands.Count(sql => sql.Contains("\"Invoices\"", StringComparison.Ordinal));

    private static IEnumerable<Guid> CustomerIdsOf(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("customerId").GetGuid());

    private async Task<Company> ACompanyAsync(string name = "acme")
    {
        var tenant = await OnboardAsync(name);
        var accountant = As(TestUsers.Accountant, tenant);
        var company = new Company(accountant, Guid.Empty, name.ToUpperInvariant());

        return company with { CustomerId = await company.AnotherCustomerAsync("A customer") };
    }

    /// <summary>One company, its accountant and the customer most of these invoices are billed to.</summary>
    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Prefix)
    {
        public async Task<Guid> AnotherCustomerAsync(string name = "Another customer")
        {
            var customer = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/customers", new
            {
                name,
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

        /// <summary>A draft of 610,00 gross, and its id.</summary>
        public async Task<Guid> DraftAsync(Guid? customer = null)
        {
            var draft = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/invoices", new
            {
                customerId = customer ?? CustomerId,
                lines = new object[]
                {
                    new { description = "Consulting", quantity = 2m, unitPrice = new { amount = 250m, currency = "EUR" }, vatRate = 22m },
                },
            }));

            return draft.GetProperty("id").GetGuid();
        }

        public async Task<Guid> IssueAsync(Guid? customer = null)
        {
            var draft = await DraftAsync(customer);
            await ReadSuccessAsync(await Accountant.PostAsync($"api/invoices/{draft}/issue", null));

            return draft;
        }

        public async Task PayAsync(Guid invoice, decimal amount)
            => await ReadSuccessAsync(await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/payments", new
            {
                paidOn = "2026-04-02",
                amount = new { amount, currency = "EUR" },
                reference = "BONIFICO 4471",
                method = "BankTransfer",
            }));

        /// <summary>
        ///     One page of the list. The query is named in the failure, because a test that fails saying
        ///     only what the exception was has to be run again to learn which call raised it.
        /// </summary>
        public async Task<JsonElement> ListAsync(string query)
        {
            try
            {
                return await ReadJsonAsync(await Accountant.GetAsync($"api/invoices?{query}"));
            }
            catch (Exception failure) when (failure is not Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"?{query} → {failure.GetType().Name}: {failure.Message}");
            }
        }

        public async Task<JsonElement> OutstandingAsync(string query = "")
            => await ReadJsonAsync(await Accountant.GetAsync($"api/invoices/outstanding?{query}"));
    }
}
