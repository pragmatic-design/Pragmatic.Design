using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Xlsx;
using Pragmatic.Endpoints.Responses;

namespace Showcase.Billing.Endpoints;

/// <summary>
/// Exports all invoices as an XLSX spreadsheet.
/// Demonstrates: Pragmatic.Documents integration end-to-end — <see cref="SpreadsheetBuilder"/> →
/// <see cref="SpreadsheetModel"/> → <see cref="XlsxRenderer"/>, streamed back via the endpoint
/// <see cref="FileResponse"/> contract (the SG maps it to <c>Results.File</c>).
/// XLSX is fully managed and cross-platform (no native/OS dependency), unlike the PDF renderer.
/// </summary>
[Endpoint(HttpVerb.Get, "api/invoices/export.xlsx")]
[RequirePermission(BillingPermissions.Invoice.Read)]
[ApiSummary("Export Invoices (XLSX)")]
[ApiDescription("Exports all invoices as an XLSX spreadsheet.")]
[ApiTags("Invoices")]
public partial class ExportInvoicesEndpoint : Endpoint<FileResponse>
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private IReadRepository<Invoice> _invoices = null!;

    public override async Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
    {
        var invoices = await _invoices
            .FindAsync(Spec<Invoice>.Where(i => true), ct)
            .ConfigureAwait(false);

        var model = new SpreadsheetBuilder()
            .Title("Invoices")
            .Sheet("Invoices", sheet =>
            {
                sheet.HeaderRow(
                    "Invoice Number", "Reservation Id", "Status",
                    "Currency", "Subtotal", "Tax", "Total", "Issued At");

                foreach (var invoice in invoices)
                    sheet.Row(
                        invoice.InvoiceNumber,
                        invoice.ReservationId.ToString(),
                        invoice.Status.ToString(),
                        invoice.Currency,
                        invoice.SubTotal,
                        invoice.TaxAmount,
                        invoice.TotalAmount,
                        invoice.IssuedAt.UtcDateTime);
            })
            .Build();

        var bytes = XlsxRenderer.Render(model);

        return new FileResponse(new MemoryStream(bytes), XlsxContentType, "invoices.xlsx");
    }
}
