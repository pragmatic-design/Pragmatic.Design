using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.Billing.Entities;
using Invoicing.Billing.Infrastructure.Jobs;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Email.Testing;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The three meanings of "remove": a draft is deleted, an issued invoice is voided, a
///     customer is soft-deleted. And the trail that says who did which.
/// </summary>
public sealed class RemovingWhatIsNoLongerWanted(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly TestClock _clock = new(Issued);
    private InMemoryTransport _mailbox = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock>(_clock);
        _mailbox = services.AddEmailTestHarness();
    }

    /// <summary>
    ///     A draft goes entirely: the row, its lines, and nothing of it in the answer.
    /// </summary>
    /// <remarks>
    ///     The body is asserted and not only the status. A delete that serialises the entity it removed
    ///     hands the caller whatever that entity holds — in Time off it was the hash of a password,
    ///     Out of a <c>DELETE</c>.
    /// </remarks>
    [Fact]
    public async Task ADraftIsDeleted_AndItsLinesGoWithIt()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();

        var response = await company.Accountant.DeleteAsync($"api/invoices/{draft}");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(body);
        body.Should().NotContain("tenantId").And.NotContain("billedToEmail").And.NotContain("customerCode",
            "a delete answers that it happened, not with the row it removed");
        IdsOf(await company.ListAsync("status=Draft")).Should().BeEmpty();
        (await LinesOfAsync(company.Tenant, draft)).Should().Be(0, "the lines cascade with their invoice");
    }

    [Fact]
    public async Task DeletingAnIssuedInvoice_IsRefusedWith409()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());

        var response = await company.Accountant.DeleteAsync($"api/invoices/{invoice}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "an issued invoice is voided, not deleted");
        IdsOf(await company.ListAsync("status=Issued")).Should().Equal([invoice], "and it is still there");
    }

    [Fact]
    public async Task AnIssuedInvoiceIsVoided_KeepsItsNumberAndItsPdf()
    {
        var company = await ACompanyAsync();
        var before = await company.IssueAsync();
        var invoice = Id(before);

        var voided = await company.VoidAsync(invoice, "Issued to the wrong customer");

        voided.GetProperty("status").GetString().Should().Be("Void");
        voided.GetProperty("number").GetString().Should().Be(before.GetProperty("number").GetString(),
            "a void invoice keeps its number");
        voided.GetProperty("pdfSha256").GetString().Should().Be(before.GetProperty("pdfSha256").GetString(),
            "and the document that was sent");
        voided.GetProperty("voidReason").GetString().Should().Be("Issued to the wrong customer");
        voided.GetProperty("voidedOn").GetString().Should().Be("2026-03-14");

        var download = await company.Accountant.GetAsync($"api/invoices/{invoice}/pdf");
        download.StatusCode.Should().Be(HttpStatusCode.OK, "the document a customer holds is still served");
    }

    /// <summary>
    ///     The series did not go back: the next invoice takes the following number, and the void one keeps
    ///     the number it had. A hole in the sequence is worse than a void number in it.
    /// </summary>
    [Fact]
    public async Task ANextInvoice_TakesTheFollowingNumber()
    {
        var company = await ACompanyAsync();
        await company.VoidAsync(Id(await company.IssueAsync()), "Wrong amounts");

        var second = await company.IssueAsync();

        second.GetProperty("number").GetString().Should().Be($"{company.Prefix}/2026/0002");
    }

    [Fact]
    public async Task VoidingWithoutAReason_IsRefusedWith422()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());

        var response = await company.Accountant.PostAsJsonAsync($"api/invoices/{invoice}/void", new { reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a void invoice with no reason is a document nobody can account for");
    }

    [Fact]
    public async Task VoidingAPaidInvoice_IsRefusedWith409()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());
        await company.PayAsync(invoice, 610m);

        var response = await company.Accountant.PostAsJsonAsync($"api/invoices/{invoice}/void", new { reason = "Mistake" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Should().Contain("INVOICE_IS_PAID", "the refusal says which of the two it is");
    }

    /// <summary>
    ///     A <b>partly</b> paid invoice can still be voided: only a settled one is refused.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the control, and the reason the naive option cannot be taken. Checking the loaded aggregate's <c>[Invariant]</c> methods after an action's body
    ///     would evaluate <c>AmountPaidMatchesPayments</c> here — and <c>VoidInvoiceAction</c> includes
    ///     <c>Lines</c> and not <c>Payments</c>, so the rule would read an empty collection against an
    ///     <c>AmountPaid</c> of 100 and refuse this request with a 422. A rule evaluated against data
    ///     nobody loaded refuses what it should allow.
    /// </remarks>
    [Fact]
    public async Task VoidingAPartlyPaidInvoice_IsAllowed()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());
        await company.PayAsync(invoice, 100m);

        var voided = await company.VoidAsync(invoice, "Sent to the wrong customer");

        voided.GetProperty("status").GetString().Should().Be("Void");
    }

    /// <summary>
    ///     A void invoice is not chased. The control is in the same test: the other company's invoice, in
    ///     the same state but still issued, produces its reminder.
    /// </summary>
    [Fact]
    public async Task AVoidInvoiceIsNotChased()
    {
        var voided = await ACompanyAsync("acme");
        var chased = await ACompanyAsync("globex");
        await voided.VoidAsync(Id(await voided.IssueAsync()), "Issued twice");
        await chased.IssueAsync();

        _clock.SetDate(2026, 4, 20);
        await SweepAsync(voided.Tenant, new DateOnly(2026, 4, 20));
        await SweepAsync(chased.Tenant, new DateOnly(2026, 4, 20));

        RemindersTo(voided.CustomerEmail).Should().BeEmpty("nothing is owed on a void invoice");
        RemindersTo(chased.CustomerEmail).Should().ContainSingle("the control: an issued one is chased");
    }

    [Fact]
    public async Task ASoftDeletedCustomer_DisappearsFromTheListAndComesBack()
    {
        var company = await ACompanyAsync();

        await ReadSuccessAsync(await company.Accountant.DeleteAsync($"api/customers/{company.CustomerId}"));
        var afterDelete = await company.CustomersAsync();

        await ReadSuccessAsync(await company.Accountant.PostAsync($"api/customers/{company.CustomerId}/restore", null));
        var afterRestore = await company.CustomersAsync();

        afterDelete.Should().BeEmpty("a soft-deleted customer is out of every read");
        afterRestore.Should().Equal([company.CustomerId], "and comes back with the code it had");
    }

    /// <summary>
    ///     The whole purpose of freezing the customer onto the invoice, asserted: what was issued still
    ///     reads after the customer is gone from the register.
    /// </summary>
    [Fact]
    public async Task TheInvoicesOfADeletedCustomer_StillReadCorrectly()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());

        await ReadSuccessAsync(await company.Accountant.DeleteAsync($"api/customers/{company.CustomerId}"));

        var read = await company.ReadAsync(invoice);
        read.GetProperty("billedToName").GetString().Should().Be("A customer");
        read.GetProperty("customerCode").GetString().Should().NotBeEmpty();
        IdsOf(await company.ListAsync("")).Should().Equal([invoice], "the list is unchanged");
    }

    /// <summary>
    ///     And the refusal that does exist: a <em>new</em> invoice for a customer who is gone. Registry
    ///     never had to ask Billing anything — the published read simply no longer returns the row.
    /// </summary>
    [Fact]
    public async Task ADraftForADeletedCustomer_IsNotFound()
    {
        var company = await ACompanyAsync();
        await ReadSuccessAsync(await company.Accountant.DeleteAsync($"api/customers/{company.CustomerId}"));

        var response = await company.Accountant.PostAsJsonAsync("api/invoices", new
        {
            customerId = company.CustomerId,
            lines = new object[]
            {
                new { description = "Consulting", quantity = 1m, unitPrice = new { amount = 10m, currency = "EUR" }, vatRate = 22m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     The trail: who voided it, and when.
    /// </summary>
    /// <remarks>
    ///     Measured here, and worth knowing: the trail stamps its entries with the <b>application's</b>
    ///     clock — the host's <c>TimeProvider</c> comes from the same <c>IClock</c> the domain reads — so a
    ///     test that pins the clock pins the trail too. The first version of this assertion compared
    ///     <c>OccurredAt</c> against the machine's <c>UtcNow</c> and was red for that reason, which is a
    ///     better answer than the one it was looking for.
    /// </remarks>
    [Fact]
    public async Task TheTrailSaysWhoVoidedItAndWhen()
    {
        var company = await ACompanyAsync();
        var invoice = Id(await company.IssueAsync());
        await company.VoidAsync(invoice, "Issued to the wrong customer");

        var entries = await TrailOfAsync(company.Tenant, invoice);

        entries.Should().NotBeEmpty("an [Audited] entity records every change without a line of code");
        string.Join(" ", entries.Select(e => $"{e.Operation}/{e.ActorRef ?? "(none)"}"))
            .Should().Contain(TestUsers.Accountant.Id, "the trail names who acted");
        entries.Should().Contain(e => e.OccurredAt.UtcDateTime.Date == Issued.UtcDateTime.Date,
            "the trail stamps the application's clock, which this test pins — not the machine's");
    }

    private static IEnumerable<Guid> IdsOf(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());

    private static Guid Id(JsonElement invoice) => invoice.GetProperty("id").GetGuid();

    private IReadOnlyList<SentEmail> RemindersTo(string address)
        => _mailbox.SentWhere(m => m.To.Any(a => a.Address == address));

    private async Task SweepAsync(string tenant, DateOnly today)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        await services.ServiceProvider.GetRequiredService<IOverdueReminderSweep>().RunAsync(today);
    }

    /// <summary>How many lines the database still holds for an invoice.</summary>
    private async Task<int> LinesOfAsync(string tenant, Guid invoice)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        return await services.ServiceProvider.GetRequiredService<IReadRepository<InvoiceLine>>()
            .CountAsync(Spec<InvoiceLine>.Where(line => line.InvoiceId == invoice));
    }

    private async Task<IReadOnlyList<AuditEntry>> TrailOfAsync(string tenant, Guid invoice)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var page = await services.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = nameof(Invoice), TargetId = invoice.ToString() });

        return page.Entries;
    }

    private async Task<Company> ACompanyAsync(string name = "acme")
    {
        var tenant = await OnboardAsync(name);
        var accountant = As(TestUsers.Accountant, tenant);
        var email = $"billing@{tenant}.customer.test";
        var customer = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", new
        {
            name = "A customer",
            vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email,
            preferredCulture = "it-IT",
            paymentTermsDays = 30,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        }));

        return new Company(accountant, customer.GetProperty("id").GetGuid(), tenant, name.ToUpperInvariant(), email);
    }

    private sealed record Company(
        HttpClient Accountant, Guid CustomerId, string Tenant, string Prefix, string CustomerEmail)
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

        /// <summary>
        ///     An issued invoice, as the issue answered it — the full DTO, which is where the number and
        ///     the document's hash are. The list carries neither: it is a different, narrower projection.
        /// </summary>
        public async Task<JsonElement> IssueAsync()
        {
            var draft = await DraftAsync();

            return await ReadJsonAsync(await Accountant.PostAsync($"api/invoices/{draft}/issue", null));
        }

        public async Task<JsonElement> VoidAsync(Guid invoice, string reason)
            => await ReadJsonAsync(
                await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/void", new { reason }));

        public async Task PayAsync(Guid invoice, decimal amount)
            => await ReadSuccessAsync(await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/payments", new
            {
                paidOn = "2026-04-02",
                amount = new { amount, currency = "EUR" },
                reference = "BONIFICO 4471",
                method = "BankTransfer",
            }));

        public async Task<JsonElement> ListAsync(string query)
            => await ReadJsonAsync(await Accountant.GetAsync($"api/invoices?{query}"));

        /// <summary>One invoice, read back through the list: this application has no GET of one.</summary>
        public async Task<JsonElement> ReadAsync(Guid invoice)
        {
            var page = await ListAsync("");

            return page.GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetGuid() == invoice);
        }

        public async Task<IReadOnlyList<Guid>> CustomersAsync()
        {
            var page = await ReadJsonAsync(await Accountant.GetAsync("api/customers"));

            return [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())];
        }
    }
}
