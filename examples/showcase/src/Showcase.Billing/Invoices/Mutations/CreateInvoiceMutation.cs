namespace Showcase.Billing.Mutations;

/// <summary>
/// Creates a draft invoice for a confirmed reservation.
/// No [Endpoint] — internal only, invoked via IMutationInvoker from event handlers.
/// The SG auto-maps mutation properties to entity SetX() calls and calls Invoice.Create().
/// The LineItem is added in ApplyAsync (child entity creation).
/// </summary>
[Mutation(Mode = MutationMode.Create)]
public partial class CreateDraftInvoiceMutation : Mutation<Invoice>
{
    public required Guid ReservationId { get; init; }
    public required Guid GuestId { get; init; }
    public required decimal SubTotal { get; init; }
    public required decimal TaxAmount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>
    /// Custom logic: adds the room charge line item after auto-mapped properties.
    /// Auto-mapping (ReservationId, GuestId, TotalAmount, etc.) runs automatically
    /// via ApplyToEntity before this method — no need for manual SetX calls.
    /// </summary>
    public override Task<Result<Invoice, IError>> ApplyAsync(Invoice entity, CancellationToken ct = default)
    {
        // The invoice key is the relation's, written by EF when the item joins the collection.
        var lineItem = LineItem.Create(SubTotal, SubTotal);
        lineItem.SetDescription("Room charge");
        entity.LineItems.Add(lineItem);

        return Task.FromResult(Result<Invoice, IError>.Success(entity));
    }
}
