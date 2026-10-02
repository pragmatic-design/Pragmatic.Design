using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Invoicing.Billing.Infrastructure.Jobs;
using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email;
using Pragmatic.Email.Testing;
using Pragmatic.Jobs;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     Every morning the overdue invoices are chased: one e-mail per invoice, from the company
///     that issued it, with the document that was issued attached, and never the same one twice in a week.
/// </summary>
/// <remarks>
///     <para>
///         The clock is the test's: an invoice is issued on the 14th of March, falls due thirty days later,
///         and the sweep is asked to run on a day the test chooses. Nothing here reads the machine's date.
///     </para>
///     <para>
///         This class has a database of its own (<c>ConnectionStringAsync</c>), because the job reads
///         everything there is: on the suite's shared database it would also chase the companies the other
///         classes left behind. The assertions still name their own recipients — that is what makes them
///         readable — and the fan-out test can additionally count the whole mailbox.
///     </para>
/// </remarks>
public sealed class ChasingOverdueInvoices(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AfterTheDueDate = new(2026, 4, 20);

    private readonly TestClock _clock = new(Issued);
    private InMemoryTransport _mailbox = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IClock>(_clock);
        _mailbox = services.AddEmailTestHarness();
    }

    /// <summary>
    ///     A database of its own, because the job here reads <b>everything</b>: it fans out over every
    ///     active company, so on the shared database it would also chase the companies the other classes
    ///     left behind. The assertions name their own recipients and hold either way; the work would grow
    ///     with the suite, and `EachTenantIsChasedWithItsOwnInvoices` could then no longer count.
    /// </summary>
    protected override Task<string> ConnectionStringAsync(PostgresFixture shared)
        => shared.CreateDatabaseAsync();

    /// <summary>
    ///     The schedule is an assertion of its own: a malformed cron expression builds cleanly and is
    ///     skipped at startup with a log line, so nothing else in this suite would notice.
    /// </summary>
    [Fact]
    public void TheRecurringDefinition_IsDeclared()
    {
        var definitions = Services.GetRequiredService<IRecurringJobProvider>().GetDefinitions();

        var chase = definitions.Single(d => d.Id == "chase-overdue-invoices");
        chase.CronExpression.Should().Be("0 7 * * *");
        chase.TimeZoneId.Should().Be("Europe/Rome");
    }

    [Fact]
    public async Task AnOverdueInvoice_IsChasedOnce()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        var swept = await SweepAsync(company.Tenant, AfterTheDueDate);

        swept.IsSuccess.Should().BeTrue();
        var reminders = RemindersTo(company.CustomerEmail);
        reminders.Should().ContainSingle();

        var message = reminders[0].Message;
        message.From.Address.Should().Be(company.SenderEmail, "the customer knows the company, not us");
        message.Subject.Should().Contain(invoice.GetProperty("number").GetString()!);
        message.Attachments.Should().ContainSingle();
        message.Attachments[0].ContentType.Should().Be("application/pdf");
    }

    /// <summary>
    ///     The attachment is the file that was issued, byte for byte — the test that fails the day somebody
    ///     renders the document again instead of reading the one that was stored.
    /// </summary>
    [Fact]
    public async Task TheAttachedPdf_IsTheOneIssued()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssuedInvoiceAsync();

        await SweepAsync(company.Tenant, AfterTheDueDate);

        var attached = RemindersTo(company.CustomerEmail)[0].Message.Attachments[0];
        Convert.ToHexString(SHA256.HashData(attached.Data.Span))
            .Should().Be(invoice.GetProperty("pdfSha256").GetString(),
                "the reminder attaches the document the issue stored, never a fresh rendering");

        // The other half of the same claim, and the hash alone cannot make it: a fresh rendering of the
        // same invoice has the same bytes, so it would pass the assertion above and leave a second
        // object behind. Countable only because the store is the in-memory double.
        Files.Count.Should().Be(1,
            "one invoice was issued and one document stored; the sweep read it rather than rendering "
            + "and storing another");
    }

    [Fact]
    public async Task RunningTheSweepTwiceTheSameDay_SendsOneEmail()
    {
        var company = await ACompanyAsync();
        await company.IssuedInvoiceAsync();

        await SweepAsync(company.Tenant, AfterTheDueDate);
        await SweepAsync(company.Tenant, AfterTheDueDate);

        RemindersTo(company.CustomerEmail).Should().ContainSingle("a daily job is not a daily e-mail");
    }

    [Fact]
    public async Task EightDaysLater_TheCustomerIsChasedAgain()
    {
        var company = await ACompanyAsync();
        await company.IssuedInvoiceAsync();

        await SweepAsync(company.Tenant, AfterTheDueDate);
        await SweepAsync(company.Tenant, AfterTheDueDate.AddDays(8));

        RemindersTo(company.CustomerEmail).Should().HaveCount(2, "a week later it is chased again");
    }

    /// <summary>
    ///     The control. Without it a sweep that selected every invoice would pass every test above.
    /// </summary>
    [Fact]
    public async Task AnInvoiceNotYetDue_AndAPaidOne_AreNotChased()
    {
        var company = await ACompanyAsync();
        var paid = await company.IssuedInvoiceAsync();
        await company.PayAsync(paid.GetProperty("id").GetGuid(), 610m);
        await company.IssuedInvoiceAsync();

        // The day before the invoices fall due: one is settled, the other is not owed yet.
        var swept = await SweepAsync(company.Tenant, new DateOnly(2026, 4, 12));

        swept.Value.Should().Be(0);
        RemindersTo(company.CustomerEmail).Should().BeEmpty();
    }

    /// <summary>
    ///     The whole reason this is a job and not a query: each company is chased with its own invoices,
    ///     from its own address.
    /// </summary>
    /// <remarks>
    ///     The <b>job</b> runs here, not the sweep — the fan-out is what is being asserted — and it is
    ///     invoked directly rather than waited for: a test that went through the background service would
    ///     also be asserting the polling interval and the lease, and would fail for reasons that have
    ///     nothing to do with a reminder.
    /// </remarks>
    [Fact]
    public async Task EachTenantIsChasedWithItsOwnInvoices()
    {
        var acme = await ACompanyAsync("acme");
        var globex = await ACompanyAsync("globex");
        var theirs = await acme.IssuedInvoiceAsync();
        var ours = await globex.IssuedInvoiceAsync();

        _clock.Set(AfterTheDueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        await RunTheJobAsync();

        var toAcme = RemindersTo(acme.CustomerEmail);
        var toGlobex = RemindersTo(globex.CustomerEmail);

        _mailbox.Sent.Should().HaveCount(2,
            "two companies, one overdue invoice each — and this class has a database of its own, so there "
            + "is nothing else in it to chase");
        toAcme.Should().ContainSingle();
        toGlobex.Should().ContainSingle();
        toAcme[0].Message.From.Address.Should().Be(acme.SenderEmail);
        toGlobex[0].Message.From.Address.Should().Be(globex.SenderEmail);
        toAcme[0].Message.Subject.Should().Contain(theirs.GetProperty("number").GetString()!);
        toGlobex[0].Message.Subject.Should().Contain(ours.GetProperty("number").GetString()!);
    }

    /// <summary>
    ///     Outside a tenant scope the sweep declines, and that is the difference this test pins: the
    ///     generated filter is fail-closed, so a sweep that trusted its own query would read zero rows,
    ///     send nothing, and report success — for ever, and to everybody.
    /// </summary>
    [Fact]
    public async Task ASweepWithNoTenantResolved_ChasesNobody()
    {
        var company = await ACompanyAsync();
        await company.IssuedInvoiceAsync();

        var swept = await SweepAsync(tenant: null, AfterTheDueDate);

        swept.IsFailure.Should().BeTrue("counting to zero is what this must not do");
        swept.Error.Code.Should().Be("NO_TENANT_RESOLVED");
        _mailbox.Sent.Should().BeEmpty();
    }

    private IReadOnlyList<SentEmail> RemindersTo(string address)
        => _mailbox.SentWhere(m => m.To.Any(a => a.Address == address));

    private async Task<Result<int, Invoicing.Billing.Errors.NoTenantResolvedError>> SweepAsync(
        string? tenant, DateOnly today)
    {
        using var scope = tenant is null ? null : TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        return await services.ServiceProvider
            .GetRequiredService<IOverdueReminderSweep>()
            .RunAsync(today);
    }

    private async Task RunTheJobAsync()
    {
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        await services.ServiceProvider.GetRequiredService<ChaseOverdueInvoicesJob>().ExecuteAsync(
            new JobContext(Guid.NewGuid(), typeof(ChaseOverdueInvoicesJob).FullName!, Issued, 0, 1),
            CancellationToken.None);
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

        return new Company(accountant, customer.GetProperty("id").GetGuid(), tenant, email);
    }

    /// <summary>One company of this test, with the two addresses its reminders travel between.</summary>
    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Tenant, string CustomerEmail)
    {
        /// <summary>The address the company's own mail is sent from, as onboarding set it.</summary>
        public string SenderEmail => $"billing@{Tenant}.test";

        /// <summary>An issued invoice of 610,00 gross, due thirty days after it was issued.</summary>
        public async Task<JsonElement> IssuedInvoiceAsync()
        {
            var draft = await ReadJsonAsync(await Accountant.PostAsJsonAsync("api/invoices", new
            {
                customerId = CustomerId,
                lines = new object[]
                {
                    new { description = "Consulting", quantity = 2m, unitPrice = new { amount = 250m, currency = "EUR" }, vatRate = 22m },
                },
            }));

            return await ReadJsonAsync(
                await Accountant.PostAsync($"api/invoices/{draft.GetProperty("id").GetGuid()}/issue", null));
        }

        public async Task PayAsync(Guid invoice, decimal amount)
            => await ReadSuccessAsync(await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/payments", new
            {
                paidOn = "2026-04-02",
                amount = new { amount, currency = "EUR" },
                reference = "BONIFICO 4471",
                method = "BankTransfer",
            }));
    }
}
