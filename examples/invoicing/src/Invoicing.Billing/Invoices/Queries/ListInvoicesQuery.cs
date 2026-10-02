namespace Invoicing.Billing.Invoices.Queries;

/// <summary>
///     The company's invoices, a page at a time: by status, by customer, by the period they were issued
///     in, by what the accountant remembers of the number or the customer's name — and, if asked, only the
///     ones that are late.
/// </summary>
/// <remarks>
///     Every filter given narrows the page; none given, it is all of them. The company is in none of them:
///     the tenant filter is, which is why this query has no <c>TenantId</c> property to forget.
/// </remarks>
[Query<Invoice, InvoiceListItemDto>(Paged = true)]
[RequirePermission(BillingPermissions.Invoice.Read)]
[Endpoint(HttpVerb.Get, "api/invoices")]
public partial class ListInvoicesQuery
{
    [Filter]
    public InvoiceStatus? Status { get; init; }

    [Filter]
    public Guid? CustomerId { get; init; }

    /// <summary>Issued on this day or after it.</summary>
    [Filter(MapTo = nameof(Invoice.IssuedOn), Operator = FilterOperator.GreaterOrEqual)]
    public DateOnly? IssuedFrom { get; init; }

    /// <summary>Issued on this day or before it.</summary>
    [Filter(MapTo = nameof(Invoice.IssuedOn), Operator = FilterOperator.LessOrEqual)]
    public DateOnly? IssuedTo { get; init; }

    /// <summary>
    ///     One box over the number and the name the invoice was billed to.
    /// </summary>
    /// <remarks>
    ///     <c>IgnoreCase</c> because PostgreSQL compares as written: without it "rossi" does not find
    ///     "Rossi", and the box looks broken to everybody but the person who typed it exactly right.
    /// </remarks>
    [SearchAcross(nameof(Invoice.Number), nameof(Invoice.BilledToName), IgnoreCase = true)]
    public string? Search { get; init; }

    /// <summary>Only the invoices that are late (<c>true</c>), or only the ones that are not (<c>false</c>).</summary>
    [BindSpecification]
    public bool? Overdue { get; init; }

    /// <summary>
    ///     Today, from the application's clock: the invoker writes it, and no caller chooses which day
    ///     "late" is measured against.
    /// </summary>
    [FromClock]
    public DateOnly Today { get; private set; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? IssuedOnSort { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? NumberSort { get; init; }

    /// <summary>The entity's own rule, named rather than rewritten here.</summary>
    public Specification<Invoice>? OnlyOverdue => Overdue switch
    {
        true => InvoiceComputedFilters.IsOverdueOnSpec(Today),
        false => !InvoiceComputedFilters.IsOverdueOnSpec(Today),
        null => null
    };
}
