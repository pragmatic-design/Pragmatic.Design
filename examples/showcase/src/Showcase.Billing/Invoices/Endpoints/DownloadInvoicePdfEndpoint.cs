using Microsoft.EntityFrameworkCore;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Endpoints.Responses;
using Showcase.Booking;
using Showcase.Booking.Dtos;

namespace Showcase.Billing.Endpoints;

/// <summary>
///     The invoice as a PDF: <c>templates/invoice.pdxdoc</c>, in the guest's language.
/// </summary>
/// <remarks>
///     <para>
///         The layout and the wording are the template's. This endpoint decides the two things a template
///         cannot: the language — the guest's (<c>PreferredLanguage</c>, asked of Booking), not the language of
///         whoever downloads it — and the values the template may name, written down in
///         <see cref="DataOf" />.
///     </para>
///     <para>
///         Rendered on each request: the Showcase keeps no issued copy. An application where an invoice
///         is a legal document stores the bytes once and serves those (the Invoicing example), because a
///         document that can change after it was sent is not the document that was sent.
///     </para>
///     <para>
///         An invoice of another tenant is not found: it is read through the ordinary tenant-filtered
///         repository, which is what makes the 404 true.
///     </para>
/// </remarks>
[Endpoint(HttpVerb.Get, "/{id}/pdf")]
[EndpointGroup<InvoicesGroup>]
[RequirePermission(BillingPermissions.Invoice.Read)]
[ApiSummary("Download Invoice (PDF)")]
[ApiDescription("Renders the invoice from its template in the guest's language.")]
[ApiTags("Invoices")]
public partial class DownloadInvoicePdfEndpoint : Endpoint<FileResponse>
{
    public const string Template = "invoice.pdxdoc";

    private IReadRepository<Invoice> _invoices = null!;
    private IPdxTemplates _templates = null!;
    private IBookingActions _booking = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
    {
        var invoice = await _invoices.Query()
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.PersistenceId == Id, ct)
            .ConfigureAwait(false);

        if (invoice is null)
            return Result<FileResponse>.Failure(NotFoundError.For<Guid>("Invoice", Id));

        // The guest lives in Booking: asked through its boundary, the way Billing asks it anything — a
        // local call in one host, an HTTP call when Booking is a service of its own. An invoice with no
        // one to bill is not an invoice: a guest Booking cannot find is a 404, not a blank where the name
        // should be.
        var guest = await _booking.Guests.GetGuest(invoice.GuestId, ct).ConfigureAwait(false);
        if (guest.IsFailure)
            return Result<FileResponse>.Failure(NotFoundError.For<Guid>("Guest", invoice.GuestId));

        var document = await ComposeAsync(_templates, invoice, guest.Value, ct).ConfigureAwait(false);
        var pdf = PdfRenderer.Render(document.Model);

        return new FileResponse(new MemoryStream(pdf), "application/pdf", $"{invoice.InvoiceNumber}.pdf");
    }

    /// <summary>The invoice's document, in the guest's language — the language is the guest's, never the caller's.</summary>
    public static async Task<ComposedDocument> ComposeAsync(
        IPdxTemplates templates, Invoice invoice, GuestDto guest, CancellationToken ct = default)
    {
        var language = guest.PreferredLanguage is { Length: > 0 } theirs ? theirs : "en";

        return await templates.DocumentAsync(Template, language, DataOf(invoice, guest), ct).ConfigureAwait(false);
    }

    /// <summary>The roots the template writes against: <c>invoice</c>, <c>guest</c>, <c>lines</c>.</summary>
    public static TemplateDataContext DataOf(Invoice invoice, GuestDto guest)
        => new TemplateDataContext()
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = invoice.InvoiceNumber,
                ["issuedAt"] = invoice.IssuedAt,
                ["dueDate"] = invoice.DueDate,
                ["subTotal"] = Money.From(invoice.SubTotal, invoice.Currency),
                ["tax"] = Money.From(invoice.TaxAmount, invoice.Currency),
                ["total"] = Money.From(invoice.TotalAmount, invoice.Currency),
            })
            .AddSource("guest", new Dictionary<string, object?>
            {
                ["name"] = $"{guest.FirstName} {guest.LastName}",
            })
            .AddSource("lines", invoice.LineItems
                .Select(line => new Dictionary<string, object?>
                {
                    ["description"] = line.Description,
                    ["quantity"] = line.Quantity,
                    ["unitPrice"] = Money.From(line.UnitPrice, invoice.Currency),
                    ["total"] = Money.From(line.TotalPrice, invoice.Currency),
                })
                .ToList());
}
