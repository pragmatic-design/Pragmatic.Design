namespace Invoicing.Billing.Payments.Actions;

/// <summary>
///     Removes a payment recorded by mistake: what is owed goes back up, and an invoice that was settled by
///     it is issued again.
/// </summary>
/// <remarks>
///     The row is deleted through its own repository <em>and</em> taken out of the invoice's collection.
///     Both, on purpose: the relationship is <c>Restrict</c>, so letting the row go as an orphan of the
///     collection is an error and not a delete — and the amount paid is recomputed from the collection this
///     operation leaves behind.
/// </remarks>
[DomainAction]
[RequirePermission(BillingPermissions.Payment.Record)]
[Endpoint(HttpVerb.Delete, "api/invoices/{invoiceId}/payments/{id}")]
[LoadEntity<Invoice>(nameof(InvoiceId), Include = $"{nameof(Invoice.Lines)},{nameof(Invoice.Payments)}", FieldName = "_invoice")]
public partial class DeletePaymentAction : DomainAction<InvoiceDto, NotFoundError>
{
    private IRepository<Payment> _payments = null!;

    [FromRoute]
    public required Guid InvoiceId { get; init; }

    [FromRoute]
    public required Guid Id { get; init; }

    public override Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct = default)
    {
        var payment = _invoice.Payments.FirstOrDefault(p => p.Id == Id);
        if (payment is null)
            return Task.FromResult(Result<InvoiceDto, IError>.Failure(NotFoundError.For<Guid>("Payment", Id)));

        var removed = _invoice.RemovePayment(payment);
        if (removed.IsFailure)
            return Task.FromResult(Result<InvoiceDto, IError>.Failure(removed.Error));

        _payments.Remove(payment);

        return Task.FromResult(Result<InvoiceDto, IError>.Success(InvoiceDto.FromEntity(_invoice)));
    }
}
