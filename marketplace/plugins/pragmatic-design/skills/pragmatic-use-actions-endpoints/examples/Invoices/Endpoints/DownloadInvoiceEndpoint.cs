using Pragmatic.Endpoints.Responses;
using Pragmatic.Storage;

namespace Invoicing.Billing.Invoices.Endpoints;

/// <summary>
///     Downloads the document of an issued invoice — the very bytes made the day it was issued.
/// </summary>
/// <remarks>
///     Never re-rendered: a document that changes is not a document. A draft has no file and answers 404,
///     and so does an invoice of another company: the invoice is read through the ordinary tenant-filtered
///     repository, and the file is fetched by the key that invoice carries.
/// </remarks>
[Endpoint(HttpVerb.Get, "api/invoices/{id}/pdf")]
[RequirePermission(BillingPermissions.Invoice.Download)]
public partial class DownloadInvoiceEndpoint : Endpoint<FileResponse, NotFoundError>
{
    private IReadRepository<Invoice> _invoices = null!;
    private IFileStorage _files = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct).ConfigureAwait(false);

        if (invoice?.PdfKey is null)
            return NotFoundError.For<Guid>("InvoiceDocument", Id);

        var content = await _files
            .GetAsync(new Uri(invoice.PdfKey, System.UriKind.RelativeOrAbsolute), ct)
            .ConfigureAwait(false);

        if (content is null)
            return NotFoundError.For<Guid>("InvoiceDocument", Id);

        return new FileResponse(content, "application/pdf", InvoiceStorageKey.FileNameFor(invoice.Number!))
        {
            ETag = invoice.PdfSha256,
        };
    }
}
