using Pragmatic.Endpoints.Responses;
using Showcase.Billing.Dtos;

namespace Showcase.Billing.Endpoints;

/// <summary>
/// Exports the invoices as CSV — the format an accounts department asks for.
/// Demonstrates: <see cref="InvoiceCsvRow"/>'s generated serializer, <c>InvoiceCsvRow.Csv</c>.
/// </summary>
/// <remarks>
/// The sibling <see cref="ExportInvoicesEndpoint"/> builds an XLSX by listing its header row and its
/// cells in the same order, by hand. Here the columns live on the row type and the writer is
/// generated from them, so the header and the values cannot fall out of step — which is the whole
/// difference between the two exports and the reason both are in this example.
/// </remarks>
[Endpoint(HttpVerb.Get, "api/invoices/export.csv")]
[RequirePermission(BillingPermissions.Invoice.Read)]
[ApiSummary("Export Invoices (CSV)")]
[ApiDescription("Exports all invoices as a CSV file, with the columns the row type declares.")]
[ApiTags("Invoices")]
public partial class ExportInvoicesCsvEndpoint : Endpoint<FileResponse>
{
    private const string CsvContentType = "text/csv";

    private IReadRepository<Invoice> _invoices = null!;

    public override async Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
    {
        var invoices = await _invoices
            .FindAsync(Spec<Invoice>.Where(i => true), ct)
            .ConfigureAwait(false);

        var rows = invoices
            .OrderBy(invoice => invoice.InvoiceNumber, StringComparer.Ordinal)
            .Select(invoice => new InvoiceCsvRow
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                IssuedAt = invoice.IssuedAt,
                TotalAmount = invoice.TotalAmount,
                Currency = invoice.Currency,
                Status = invoice.Status.ToString()
            })
            .ToList();

        var bytes = InvoiceCsvRow.Csv.WriteToArray(rows);

        return new FileResponse(new MemoryStream(bytes), CsvContentType, "invoices.csv");
    }
}
