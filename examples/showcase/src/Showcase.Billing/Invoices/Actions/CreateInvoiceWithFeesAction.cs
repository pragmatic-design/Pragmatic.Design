namespace Showcase.Billing.Actions;

/// <summary>
/// Creates an invoice with its line items and service fees, in one transaction.
/// </summary>
/// <remarks>
/// It builds the graph itself and lets the invoker commit once — the shape for rows that are one fact.
/// It carried [CompositeAction] and was documented as demonstrating it, which it never did: a
/// steps-composite orchestrates step <em>properties</em>, and this declares none, so the attribute
/// generated nothing and the work here was always the body's. PRAG0427 now says so out loud; the real
/// demonstration of the pattern is CreateAmenityPairAction in Catalog.
/// </remarks>
[DomainAction(Internal = true)]
[RequirePermission(BillingPermissions.Invoice.Create)]
public partial class CreateInvoiceWithFeesAction : DomainAction<Guid>
{
    private IRepository<Invoice> _invoices = null!;
    private IClock _clock = null!;

    public required Guid ReservationId { get; init; }
    public required Guid GuestId { get; init; }
    public required decimal RoomCharge { get; init; }
    public required string Currency { get; init; }

    /// <summary>Optional service fees to include atomically.</summary>
    /// <remarks>
    ///     ⚠️ Each element is validated, and <b>no attribute here says so</b>: the generated
    ///     validator walks a collection whose element type is an <c>ISyncValidator</c>, and the
    ///     rules on <see cref="ServiceFeeRequest" /> are what make it one. Until those rules existed
    ///     a fee with no name and a negative amount was accepted and <em>lowered</em> the subtotal —
    ///     a discount nobody granted, through a field meant to add a charge.
    ///     <para>
    ///         <c>[ValidateElements]</c> would change nothing here: the transform reads
    ///         <c>hasValidateElements || (isCollection &amp;&amp; elementIsValidatable)</c>, and the
    ///         second half is already true. Its one setting that does something is
    ///         <c>StopOnFirstError</c>, and a caller sending twenty fees wants all the problems at
    ///         once, not the first.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<ServiceFeeRequest>? ServiceFees { get; init; }

    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Step 1: Calculate totals (need fees sum before creating invoice)
        var totalFees = ServiceFees is { Count: > 0 }
            ? ServiceFees.Sum(f => f.Amount)
            : 0m;
        var subTotal = RoomCharge + totalFees;
        var taxAmount = Invoice.CalculateTax(subTotal);
        var totalAmount = subTotal + taxAmount;

        // Step 2: Create invoice with required properties
        var invoice = Invoice.Create(ReservationId, GuestId, subTotal, taxAmount, totalAmount, now);
        // No invoice number written here. This action creates through the repository, not under a
        // create mutation, and GeneratedValueInterceptor still fills the number at SaveChanges, in the
        // declared format and off the declared sequence.
        invoice.SetCurrency(Currency);
        // Status starts at its [InitialState] (Draft) by construction — no SetStatus needed. Setting it
        // here would only repeat the initial state through the raw setter, which bypasses the state
        // machine; on a new entity it is a no-op.
        invoice.SetDueDate(now.AddDays(30));

        // Step 3: Add room charge line item
        // The invoice key is the relation's, written by EF when the item joins the collection.
        var lineItem = LineItem.Create(RoomCharge, RoomCharge);
        lineItem.SetDescription("Room charge");
        invoice.LineItems.Add(lineItem);

        // Step 4: Add service fees atomically (persisted via navigation collection)
        if (ServiceFees is { Count: > 0 })
        {
            foreach (var feeReq in ServiceFees)
            {
                var fee = new ServiceFee();
                fee.SetServiceName(feeReq.ServiceName);
                fee.SetServiceDate(feeReq.ServiceDate ?? now);
                fee.SetAmount(feeReq.Amount);
                fee.SetCurrency(Currency);
                fee.SetReason(feeReq.ServiceName);
                fee.SetInvoiceId(invoice.Id);
                invoice.Fees.Add(fee);
            }
        }

        _invoices.Add(invoice);

        // All entities saved in a single transaction by the composite invoker
        return Task.FromResult<Result<Guid, IError>>(invoice.Id);
    }
}
