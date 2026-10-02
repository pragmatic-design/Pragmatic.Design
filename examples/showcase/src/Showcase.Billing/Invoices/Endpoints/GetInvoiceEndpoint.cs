using Showcase.Billing.Dtos;
using Showcase.Booking;

namespace Showcase.Billing.Endpoints;

/// <summary>
/// Retrieves a single invoice by ID.
/// Demonstrates: <see cref="InvoiceSummaryDto"/> — <c>MapProperty(Format)</c>,
/// <c>MapConverter</c> for money formatting, localized <c>StatusLabel</c> from <c>T.Invoice.Status</c>.
/// </summary>
/// <remarks>
/// Two audiences ask for one invoice, and neither is a subset of the other: billing, who work in
/// this boundary, and the front desk at checkout, who hold the reservation's read permission and
/// nothing of billing's. <c>[RequireAnyPermission]</c> is the OR that keeps that one route — the
/// alternative is a second endpoint returning the same DTO, which is how two routes come to drift.
/// ⚠️ It is an OR and not a widening: a caller with neither is still refused, which is what the
/// third case of <c>TheInvoiceTwoDesksAskFor</c> measures.
/// </remarks>
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<InvoicesGroup>]
[RequireAnyPermission(BillingPermissions.Invoice.Read, BookingPermissions.Reservation.Read)]
[ApiSummary("Get Invoice")]
[ApiDescription("Retrieves an invoice by its unique identifier.")]
[ApiTags("Invoices")]
public partial class GetInvoiceEndpoint : Endpoint<InvoiceSummaryDto>
{
    private IReadRepository<Invoice> _invoices = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<InvoiceSummaryDto>> HandleAsync(CancellationToken ct = default)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (invoice is null)
            return Result<InvoiceSummaryDto>.Failure(NotFoundError.For<Guid>("Invoice", Id));

        return InvoiceSummaryDto.FromEntity(invoice);
    }
}
