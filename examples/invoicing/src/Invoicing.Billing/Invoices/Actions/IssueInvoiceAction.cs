using Invoicing.Billing.Errors;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Customers.Queries;
using System.Security.Cryptography;
using Invoicing.Registry.Dtos;
using Invoicing.Registry.Organizations.Queries;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Pdf;
using Pragmatic.Storage;

namespace Invoicing.Billing.Invoices.Actions;

/// <summary>
///     Issues a draft: it takes the company's next number for the year, freezes the customer, and becomes
///     a document that cannot be changed.
/// </summary>
/// <remarks>
///     <para>
///         The lines are asked for by name — <c>Include</c> — because a navigation nobody includes arrives
///         empty and the totals would be recomputed from nothing.
///     </para>
///     <para>
///         The number is a row of <c>InvoiceNumberSeries</c>, per company and per year, incremented under a
///         concurrency check. Two issues racing in the same company make one of them fail that check, and
///         it comes back as a 409 the caller retries — never a number reused or skipped.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Issue)]
[Endpoint(HttpVerb.Post, "api/invoices/{id}/issue")]
[TransitionsTo<InvoiceStatus>(InvoiceStatus.Issued, When = TransitionTiming.ByBody)]
[LoadEntity<Invoice>(nameof(Id), Include = nameof(Invoice.Lines), FieldName = "_invoice")]
public partial class IssueInvoiceAction : DomainAction<InvoiceDto, InvoiceNotDraftError, CustomerNotFoundError>
{
    // _invoice is declared by the generator from [LoadEntity(FieldName = "_invoice")]: declaring it here
    // too is CS0102, and a concrete type in an action's field is PRAG0419 — the loaded row is not a service.
    private IRegistryReads _registry = null!;
    private IRepository<InvoiceNumberSeries> _series = null!;
    private IFileStorage _files = null!;
    private IPdxTemplates _templates = null!;

    public required Guid Id { get; init; }

    /// <summary>The day the invoice is issued: the application's clock, never the caller's.</summary>
    [FromClock]
    public DateOnly IssuedOn { get; private set; }

    public override async Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct = default)
    {
        // First, and before a number is taken: an invoice already issued must not consume one on its way
        // to being refused, or the series would have a hole nobody could explain.
        // ⚠️ Not `ValidateLoaded()`, although this is a rule on the loaded row: that hook returns a
        // ValidationError, which is a 422, and the refusal a client keys on here is a 409 with a code.
        if (_invoice.Status != InvoiceStatus.Draft)
            return new InvoiceNotDraftError();

        var customers = await _registry
            .GetCustomerBillingDetails(new GetCustomerBillingDetailsQuery { CustomerId = _invoice.CustomerId }, ct)
            .ConfigureAwait(false);

        if (customers.Count == 0)
            return new CustomerNotFoundError();

        var issuers = await _registry
            .GetOrganizationBillingDetails(new GetOrganizationBillingDetailsQuery { Slug = _invoice.TenantId }, ct)
            .ConfigureAwait(false);

        if (issuers.Count == 0)
            return new CustomerNotFoundError();

        var number = await NextNumberAsync(issuers[0].InvoiceNumberPrefix, ct).ConfigureAwait(false);

        var issued = _invoice.Issue(number, IssuedOn, customers[0].PaymentTermsDays, customers[0]);
        if (issued.IsFailure)
            return Result<InvoiceDto, IError>.Failure(issued.Error);

        await RenderAndStoreAsync(issuers[0], ct).ConfigureAwait(false);

        return InvoiceDto.FromEntity(_invoice);
    }

    /// <summary>
    ///     Renders the document once, stores it under the company's container, and records where it is,
    ///     how long it is and what it hashes to.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The bytes are written before the transaction commits, so a failed commit leaves a file
    ///     nobody references. That is deliberate: the alternative — writing after the commit, from an
    ///     event handler — is a second write to the row just written and a failure with nowhere to report
    ///     it, and the key holds a number that is never reused.
    /// </remarks>
    private async Task RenderAndStoreAsync(OrganizationBillingDetailsDto issuer, CancellationToken ct)
    {
        // The document is the customer's, so it is made in the customer's language — the one frozen onto
        // the invoice at issue, not the one this request asked for. ForTheCustomerAsync passes it on.
        var bytes = PdfRenderer.Render(
            await InvoiceDocument.ForTheCustomerAsync(_templates, _invoice, issuer, ct).ConfigureAwait(false));

        using var content = new MemoryStream(bytes);
        var stored = await _files.SaveAsync(
                content,
                InvoiceStorageKey.FileNameFor(_invoice.Number!),
                InvoiceStorageKey.ContainerFor(_invoice.TenantId, IssuedOn.Year),
                ct)
            .ConfigureAwait(false);

        _invoice.RecordDocument(stored.ToString(), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    ///     <c>{PREFIX}/{year}/{0001}</c>, from the company's series for that year — created the first time
    ///     the company issues anything in it.
    /// </summary>
    private async Task<string> NextNumberAsync(string prefix, CancellationToken ct)
    {
        var series = await _series
            .FirstOrDefaultAsync(InvoiceNumberSeriesSpecifications.ForYear(IssuedOn.Year), ct)
            .ConfigureAwait(false);

        if (series is null)
        {
            series = InvoiceNumberSeries.StartingIn(IssuedOn.Year);
            _series.Add(series);
        }

        return $"{prefix}/{IssuedOn.Year}/{series.Take():D4}";
    }
}
