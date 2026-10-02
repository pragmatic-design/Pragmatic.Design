using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.Billing.Entities;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Entities;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The five domain events the manifest declares, raised where the move happens, and what
///     a handler does with one.
/// </summary>
/// <remarks>
///     <para>
///         The manifest declared five events and both modules declared <c>features: [… events]</c> while
///         nothing in <c>src</c> raised, declared or handled any of them — so the manifest taught the
///         reader that <c>events:</c> is decoration. So they are built.
///     </para>
///     <para>
///         What is asserted here is the <b>effect</b>, never the existence: each move must leave an entry
///         in the audit trail that only a handler could have written, and a move that did not happen must
///         leave none. An event with no observable consequence is a declaration, and this application
///         already had five of those.
///     </para>
///     <para>
///         ⚠️ <c>[Audited]</c> is on both entities and records that a row changed, without a line of
///         code. That is not what these tests read: they read the operation names below, which say what
///         the change <em>meant</em> — which a hash of the row cannot say, and which is the whole reason
///         a handler exists beside the trait.
///     </para>
/// </remarks>
public sealed class TheEventsTheMovesRaise(PostgresFixture database) : InvoicingTestBase(database)
{
    private static readonly DateTimeOffset Today = new(2026, 5, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The operations a handler writes, by their literal names.</summary>
    /// <remarks>
    ///     Spelled out rather than read from the module's own constants: a test that reuses the constant
    ///     passes when the constant changes value, which is the one thing a name on a stored row must not
    ///     do — a trail already written keeps the old spelling.
    /// </remarks>
    private const string InvoiceIssued = "Billing.InvoiceIssued";

    private const string InvoicePaid = "Billing.InvoicePaid";
    private const string InvoiceVoided = "Billing.InvoiceVoided";
    private const string OrganizationOnboarded = "Registry.OrganizationOnboarded";
    private const string OrganizationSuspended = "Registry.OrganizationSuspended";

    /// <summary>1 × 100,00 at 22% — 122,00 gross, which is what pays an invoice of this suite in full.</summary>
    private const decimal InFull = 122m;

    private readonly TestClock _clock = new(Today);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IClock>(_clock);

    [Fact]
    public async Task IssuingAnInvoice_LeavesWhatTheMoveMeant()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();

        var operations = await OperationsOnAsync(company.Tenant, nameof(Invoice), invoice);

        operations.Should().Contain(InvoiceIssued,
            "issuing is a fact the application announces, and the handler is what records what it meant");
    }

    [Fact]
    public async Task RecordingTheLastPayment_LeavesWhatTheMoveMeant()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();
        await company.PayAsync(invoice, InFull);

        var operations = await OperationsOnAsync(company.Tenant, nameof(Invoice), invoice);

        operations.Should().Contain(InvoicePaid, "the invoice reached Paid, so the move happened");
    }

    [Fact]
    public async Task VoidingAnInvoice_LeavesWhatTheMoveMeant()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();
        await company.VoidAsync(invoice);

        var operations = await OperationsOnAsync(company.Tenant, nameof(Invoice), invoice);

        operations.Should().Contain(InvoiceVoided);
    }

    /// <summary>
    ///     The control: a draft that nobody moved raises nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the move is recorded" is satisfied by recording every write — which is what
    ///     <c>[Audited]</c> already does, and would make the three tests above pass with no event and no
    ///     handler at all.
    /// </remarks>
    [Fact]
    public async Task ADraftNobodyMoved_RaisesNothing()
    {
        var company = await ACompanyAsync();
        var draft = await company.DraftAsync();

        var operations = await OperationsOnAsync(company.Tenant, nameof(Invoice), draft);

        operations.Should().NotContain(InvoiceIssued)
            .And.NotContain(InvoicePaid)
            .And.NotContain(InvoiceVoided);
    }

    /// <summary>
    ///     A partial payment is not the move: the invoice is still owed.
    /// </summary>
    /// <remarks>
    ///     The control that separates "the state machine raises it" from "RecordPayment raises it".
    ///     <c>[RaisesEvent]</c> on the target state is the first, and it is what the manifest declares —
    ///     so a payment that leaves something owed must record no <c>invoice.paid</c>.
    /// </remarks>
    [Fact]
    public async Task APartialPayment_IsNotTheMove()
    {
        var company = await ACompanyAsync();
        var invoice = await company.IssueAsync();
        await company.PayAsync(invoice, 10m);

        var operations = await OperationsOnAsync(company.Tenant, nameof(Invoice), invoice);

        operations.Should().NotContain(InvoicePaid,
            "the invoice is still Issued, so no transition to Paid happened");
    }

    [Fact]
    public async Task OnboardingACompany_LeavesWhatTheMoveMeant()
    {
        var company = await ACompanyAsync();

        var operations = await OperationsOnAsync(company.Tenant, nameof(Organization), company.OrganizationId);

        operations.Should().Contain(OrganizationOnboarded,
            "a company entering the service is the fact Registry announces");
    }

    [Fact]
    public async Task SuspendingACompany_LeavesWhatTheMoveMeant()
    {
        var company = await ACompanyAsync();

        await ReadSuccessAsync(await As(TestUsers.PlatformAdministrator)
            .PostAsync($"api/organizations/{company.Tenant}/suspend", null));

        var operations = await OperationsOnAsync(company.Tenant, nameof(Organization), company.OrganizationId);

        operations.Should().Contain(OrganizationSuspended);
    }

    /// <summary>Every operation the trail holds for one row.</summary>
    private async Task<IReadOnlyList<string>> OperationsOnAsync(string tenant, string targetType, Guid target)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var page = await services.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = targetType, TargetId = target.ToString() });

        return [.. page.Entries.Select(e => e.Operation)];
    }

    private async Task<Company> ACompanyAsync()
    {
        var tenant = await OnboardAsync("events");
        var accountant = As(TestUsers.Accountant, tenant);
        var customer = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", new
        {
            name = "A customer",
            vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email = $"billing@{tenant}.customer.test",
            preferredCulture = "it-IT",
            paymentTermsDays = 30,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        }));

        return new Company(accountant, customer.GetProperty("id").GetGuid(), tenant, await OrganizationIdAsync(tenant));
    }

    /// <summary>The company's own row id, which the trail's entries are about.</summary>
    /// <remarks>
    ///     Read rather than taken from the onboarding's answer because the base's <c>OnboardAsync</c>
    ///     returns the slug: the id is what the audit entry carries, and the slug is what the token does.
    /// </remarks>
    private async Task<Guid> OrganizationIdAsync(string tenant)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var row = await services.ServiceProvider.GetRequiredService<IReadRepository<Organization>>()
            .FirstOrDefaultAsync(Spec<Organization>.Where(o => o.Slug == tenant));

        row.Should().NotBeNull($"the company '{tenant}' was just onboarded");
        return row!.PersistenceId;
    }

    private sealed record Company(HttpClient Accountant, Guid CustomerId, string Tenant, Guid OrganizationId)
    {
        /// <summary>A draft of 122,00 gross, and its id.</summary>
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

        public async Task<Guid> IssueAsync()
        {
            var draft = await DraftAsync();
            await ReadSuccessAsync(await Accountant.PostAsync($"api/invoices/{draft}/issue", null));

            return draft;
        }

        public async Task PayAsync(Guid invoice, decimal amount)
            => await ReadSuccessAsync(await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/payments", new
            {
                paidOn = "2026-05-06",
                amount = new { amount, currency = "EUR" },
                method = "BankTransfer",
            }));

        public async Task VoidAsync(Guid invoice)
            => await ReadSuccessAsync(await Accountant.PostAsJsonAsync($"api/invoices/{invoice}/void", new
            {
                reason = "Issued to the wrong customer",
            }));
    }
}
