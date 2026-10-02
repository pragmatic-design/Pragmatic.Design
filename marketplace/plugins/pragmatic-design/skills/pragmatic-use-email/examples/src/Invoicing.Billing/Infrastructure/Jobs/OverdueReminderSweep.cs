using Invoicing.Billing.Errors;
using Invoicing.Billing.Infrastructure.Email;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Organizations.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Email;
using Pragmatic.Documents.Markup;
using Pragmatic.MultiTenancy;
using Pragmatic.Storage;

namespace Invoicing.Billing.Infrastructure.Jobs;

/// <summary>
///     One company's overdue invoices, chased.
/// </summary>
/// <remarks>
///     <para>
///         Every read here is an ordinary read: the tenant rule is applied by the filter the generator
///         wrote, from the scope the caller opened. There is no <c>FilterMode.Background</c> anywhere in
///         this file, and that is the point — the sweep works inside one company at a time, so a mistake
///         shows up as nothing happening rather than as one customer reading another's invoices.
///     </para>
///     <para>
///         The unit of work is keyed by the boundary, as the generated registration writes it: there is no
///         unkeyed <c>IUnitOfWork</c> to resolve, and asking for one throws at construction.
///     </para>
///     <para>
///         Three of its dependencies are registered outside this compilation — <c>IRegistryReads</c> by
///         the other module, <c>IFileStorage</c> and <c>IEmailSender</c> by the host's <c>UseStorage</c>
///         and <c>UseEmail</c> — and the module registers itself all the same: each of the three says
///         who registers it, so the check behind <b>PRAG1641</b> has an answer without being handed a
///         list of names, and nothing in <c>Program.cs</c> registers this class by hand.
///     </para>
/// </remarks>
[Service<IOverdueReminderSweep>]
public sealed partial class OverdueReminderSweep(
    IRepository<Invoice> invoices,
    IRegistryReads registry,
    IFileStorage files,
    IEmailSender email,
    IPdxTemplates templates,
    ITenantContext tenant,
    [FromKeyedServices(typeof(BillingBoundary))] IUnitOfWork unitOfWork,
    ILogger<OverdueReminderSweep> logger) : IOverdueReminderSweep
{
    public async Task<Result<int, NoTenantResolvedError>> RunAsync(DateOnly today, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            LogNoTenant();
            return new NoTenantResolvedError();
        }

        var issuers = await registry
            .GetOrganizationBillingDetails(new GetOrganizationBillingDetailsQuery { Slug = tenantId }, ct)
            .ConfigureAwait(false);
        if (issuers.Count == 0)
        {
            // The scope names a company that is not in the register. The job never produces this — it takes
            // its tenants from the store — so it is a caller's mistake, and the same refusal as no tenant
            // at all: there is nobody to send from.
            LogUnknownTenant(tenantId);
            return new NoTenantResolvedError();
        }

        var overdue = await invoices
            .FindAsync(InvoiceSpecifications.ToChaseOn(today), ct)
            .ConfigureAwait(false);

        var reminded = 0;
        foreach (var invoice in overdue)
        {
            if (await ChaseAsync(issuers[0], invoice, today, ct).ConfigureAwait(false))
                reminded++;
        }

        if (reminded > 0)
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        LogSwept(tenantId, overdue.Count, reminded);

        return reminded;
    }

    /// <summary>
    ///     Sends one reminder and records it. Answers whether the customer was actually written to.
    /// </summary>
    /// <remarks>
    ///     Nothing is recorded until the transport accepted the message: a failed send leaves
    ///     <c>LastRemindedOn</c> where it was, so tomorrow's run tries again instead of the invoice going
    ///     quiet for a week on the strength of a message nobody received.
    /// </remarks>
    private async Task<bool> ChaseAsync(
        Registry.Dtos.OrganizationBillingDetailsDto issuer, Invoice invoice, DateOnly today, CancellationToken ct)
    {
        if (invoice.PdfKey is null)
        {
            LogNoDocument(invoice.Number ?? "");
            return false;
        }

        var stored = await files
            .GetAsync(new Uri(invoice.PdfKey, UriKind.RelativeOrAbsolute), ct)
            .ConfigureAwait(false);
        if (stored is null)
        {
            LogNoDocument(invoice.Number ?? "");
            return false;
        }

        using var document = new MemoryStream();
        using (stored)
            await stored.CopyToAsync(document, ct).ConfigureAwait(false);

        // In the customer's language, from the copy frozen onto the invoice — OverdueReminder passes it to
        // the template. A job has no request and no Accept-Language to take one from.
        var message = await OverdueReminder
            .BuildAsync(templates, issuer, invoice, document.ToArray(), ct)
            .ConfigureAwait(false);
        var sent = await email.SendAsync(message, ct).ConfigureAwait(false);
        if (!sent.Success)
        {
            LogSendFailed(invoice.Number ?? "", sent.ErrorMessage ?? "");
            return false;
        }

        invoice.Remind(today);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The overdue sweep ran with no tenant in scope and chased nobody.")]
    private partial void LogNoTenant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant {TenantId} names no company in the register.")]
    private partial void LogUnknownTenant(string tenantId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tenant {TenantId}: {Overdue} overdue invoice(s), {Reminded} reminder(s) sent.")]
    private partial void LogSwept(string tenantId, int overdue, int reminded);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Invoice {Number} is overdue but has no stored document: no reminder was sent.")]
    private partial void LogNoDocument(string number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The reminder for invoice {Number} was not sent: {Error}")]
    private partial void LogSendFailed(string number, string error);
}
