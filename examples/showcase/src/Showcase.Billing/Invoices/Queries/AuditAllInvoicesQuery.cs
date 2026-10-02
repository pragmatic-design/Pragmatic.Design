using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Showcase.Billing.Dtos;

namespace Showcase.Billing.Queries;

/// <summary>
///     Every invoice of the tenant, for the audit — across the data scopes that decide who normally
///     sees which.
/// </summary>
/// <remarks>
///     <para>
///         <c>Invoice</c> is <c>[HasAccessScopes]</c>, so an ordinary read answers with the rows the
///         caller's scopes reach: <c>SearchInvoicesQuery</c> shows a EUR invoice to a reader holding
///         <c>scope:billing-eu</c> and not to one holding the other bucket. An audit cannot work that
///         way — a total that depends on who is looking is not a total.
///     </para>
///     <para>
///         ⚠️ <b><c>[FilterMode(Admin)]</c> and not the generated <c>billing.invoice.view-all</c>.</b>
///         The two lift the same filters and mean different things. <c>view-all</c> is the
///         <em>caller's</em> authority and it applies on every route they touch; this says the
///         <em>operation</em> reads across scopes, whoever calls it, and its own permission is the gate.
///         An auditor gets <c>billing.invoice.audit</c> and nothing more, and their ordinary searches
///         keep answering what their scopes reach.
///     </para>
///     <para>
///         ⚠️ <b>It lifts the scopes and nothing else.</b> <c>Admin</c> drops the permission-based
///         filters — ownership and data scopes — and keeps soft delete and the tenant: an audit of this
///         tenant's invoices, not of the database's. Lifting everything is <c>QueryStrategy.Raw</c>,
///         which is a different declaration and a different answer to a different question.
///     </para>
///     <para>
///         <c>[QueryStrategy(Projection)]</c>: the rows are projected into a DTO and never written
///         back, so the read does not pay for change tracking. It is the half of the strategy that is
///         about cost rather than about visibility, and the two are declared separately because they
///         are two decisions.
///     </para>
/// </remarks>
[Query<Invoice, InvoiceSummaryDto>]
[FilterMode(FilterMode.Admin)]
[QueryStrategy(Strategy = QueryStrategy.Projection)]
[RequirePermission("billing.invoice.audit")]
[Endpoint(HttpVerb.Get, "api/invoices/audit")]
public partial class AuditAllInvoicesQuery
{
    [Filter]
    public Guid? ReservationId { get; init; }

    [Filter]
    public InvoiceStatus? Status { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? IssuedAtSort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
