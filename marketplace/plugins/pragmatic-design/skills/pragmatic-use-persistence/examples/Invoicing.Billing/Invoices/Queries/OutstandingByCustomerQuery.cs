namespace Invoicing.Billing.Invoices.Queries;

/// <summary>
///     What each customer still owes: one line per customer, the sum computed by the database.
/// </summary>
/// <remarks>
///     The query filters and the view groups — the route, the permission and the paging are the ones every
///     other query gets. Paged over a view, a page is a page of <b>groups</b>: the total is the number of
///     customers who owe something, not the number of invoices.
/// </remarks>
[Query<Invoice, OutstandingByCustomerLine>(Paged = true)]
[RequirePermission(BillingPermissions.Invoice.Read)]
[Endpoint(HttpVerb.Get, "api/invoices/outstanding")]
public partial class OutstandingByCustomerQuery
{
    /// <summary>
    ///     One customer, when the caller is looking at one; all of them otherwise.
    /// </summary>
    /// <remarks>
    ///     <c>[BindSpecification]</c>, so it feeds the rule below and is <b>not</b> also turned into a
    ///     <c>Where</c> of its own: a nullable property without it is a filter by convention, and the
    ///     condition would be applied twice.
    /// </remarks>
    [BindSpecification]
    public Guid? CustomerId { get; init; }

    /// <summary>
    ///     What is owed: issued and not settled, for this customer or for all of them.
    /// </summary>
    /// <remarks>
    ///     The "not settled" half is not optional and not a parameter. A draft owes nothing — nobody has
    ///     been asked to pay it — and a settled invoice owes nothing either; a caller able to switch that
    ///     off could ask a question whose answer means nothing.
    /// </remarks>
    public Specification<Invoice> Owed => Restricted(InvoiceSpecifications.Unpaid());

    /// <summary>
    ///     Only what is <b>late</b>, which is the list somebody chasing payments works from.
    /// </summary>
    /// <remarks>
    ///     This is what answers the question the story left open: an aggregate <em>can</em> be computed
    ///     over a set narrowed by a <c>[ComputedFilter]</c> method — the specification the generator writes
    ///     for <see cref="Invoice.IsOverdueOn" /> composes into the same <c>GROUP BY</c>, with the day
    ///     bound from the clock like anywhere else.
    /// </remarks>
    [BindSpecification]
    public bool? Overdue { get; init; }

    /// <summary>Today, from the application's clock: no caller chooses which day "late" is measured from.</summary>
    [FromClock]
    public DateOnly Today { get; private set; }

    public Specification<Invoice>? OnlyOverdue => Overdue switch
    {
        true => Restricted(InvoiceComputedFilters.IsOverdueOnSpec(Today)),
        false => Restricted(!InvoiceComputedFilters.IsOverdueOnSpec(Today)),
        null => null
    };

    /// <summary>The customer filter, when one was given: it belongs to every rule above.</summary>
    private Specification<Invoice> Restricted(Specification<Invoice> rule)
        => CustomerId is null
            ? rule
            : rule & Spec<Invoice>.Where(invoice => invoice.CustomerId == CustomerId);
}
