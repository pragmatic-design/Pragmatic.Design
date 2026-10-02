using Invoicing.Billing.Errors;

namespace Invoicing.Billing.Invoices.Actions;

/// <summary>
///     Voids an invoice issued by mistake: it keeps its number and its document, says why it is void, and
///     stops being chased.
/// </summary>
/// <remarks>
///     <para>
///         Never deleted, and its number never released. An issued invoice is a document somebody may
///         already hold; the sequence it came from has to stay explicable, and a void number in it is
///         explicable while a hole is not.
///     </para>
///     <para>
///         The two states that cannot be voided answer differently on purpose: a <b>draft</b> is deleted
///         rather than voided (<c>DELETE api/invoices/{id}</c>), and a <b>settled</b> one stands until
///         somebody reverses the payment.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Void)]
[Endpoint(HttpVerb.Post, "api/invoices/{id}/void")]
[TransitionsTo<InvoiceStatus>(InvoiceStatus.Void, When = TransitionTiming.ByBody)]
[LoadEntity<Invoice>(nameof(Id), Include = nameof(Invoice.Lines), FieldName = "_invoice")]
public partial class VoidInvoiceAction : DomainAction<InvoiceDto, InvoiceNotIssuedError, InvoiceIsPaidError>
{
    [FromRoute]
    public required Guid Id { get; init; }

    /// <summary>
    ///     Why. Required, and that is the point of the operation: a void invoice with no reason is a
    ///     document nobody can account for.
    /// </summary>
    [Required]
    [MaxLength(300)]
    public required string Reason { get; init; }

    /// <summary>The day it is voided: the application's clock, never the caller's.</summary>
    [FromClock]
    public DateOnly VoidedOn { get; private set; }

    public override Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct = default)
    {
        // ⚠️ Not `ValidateLoaded()`: that hook answers with a ValidationError, which is a 422, and these
        // two refusals are 409s a client keys on — the same deviation, for the same reason, as
        // IssueInvoiceAction and RecordPaymentAction.
        if (_invoice.Status == InvoiceStatus.Paid)
            return Refused(new InvoiceIsPaidError());

        if (_invoice.Status != InvoiceStatus.Issued)
            return Refused(new InvoiceNotIssuedError());

        var voided = _invoice.Void(Reason, VoidedOn);

        return voided.IsFailure
            ? Refused(voided.Error)
            : Task.FromResult(Result<InvoiceDto, IError>.Success(InvoiceDto.FromEntity(_invoice)));
    }

    private static Task<Result<InvoiceDto, IError>> Refused(IError error)
        => Task.FromResult(Result<InvoiceDto, IError>.Failure(error));
}
