using Showcase.Billing.Dtos;

namespace Showcase.Billing.Queries;

/// <summary>
/// Paged search for invoices.
/// Demonstrates: [Query] + [Endpoint] unified combo in Billing boundary.
/// First query endpoint for Billing — enables invoice lookup by reservation, status, or date range.
/// </summary>
[Query<Invoice, InvoiceSummaryDto>]
[RequirePermission(BillingPermissions.Invoice.Read)]
[Endpoint(HttpVerb.Get, "api/invoices/search")]
public partial class SearchInvoicesQuery
{
    [Filter]
    public Guid? ReservationId { get; init; }

    [Filter]
    public InvoiceStatus? Status { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "IssuedAt")]
    public DateTimeOffset? FromDate { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "IssuedAt")]
    public DateTimeOffset? ToDate { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? IssuedAtSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
