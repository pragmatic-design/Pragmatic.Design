using Invoicing.Billing.Errors;
using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Payments.Actions;

/// <summary>
///     The accountant records what the customer paid. Partial payments add up; the one that reaches the
///     total makes the invoice paid.
/// </summary>
/// <remarks>
///     <para>
///         The invoice comes with its payments — <c>Include</c> — because the amount paid is checked
///         against what is already there, and a navigation nobody includes arrives empty: without it every
///         payment would look like the first.
///     </para>
///     <para>
///         The answer is the invoice, not the payment: after recording one, what the caller wants to know
///         is what is still owed and whether it is settled. The payments themselves are a read of their
///         own (<c>GET api/invoices/{id}/payments</c>).
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(BillingPermissions.Payment.Record)]
[Endpoint(HttpVerb.Post, "api/invoices/{id}/payments")]
[TransitionsTo<InvoiceStatus>(InvoiceStatus.Paid, When = TransitionTiming.ByBody, IsConditional = true)]
[LoadEntity<Invoice>(nameof(Id), Include = $"{nameof(Invoice.Lines)},{nameof(Invoice.Payments)}", FieldName = "_invoice")]
public partial class RecordPaymentAction
    : DomainAction<InvoiceDto, InvoiceNotIssuedError, OverpaymentError, CurrencyMismatchError>
{
    [FromRoute]
    public required Guid Id { get; init; }

    /// <summary>The day the money arrived, which is the customer's and not this server's.</summary>
    public required DateOnly PaidOn { get; init; }

    /// <summary>
    ///     How much arrived. <c>[PositiveMoney]</c> here and not on the entity: on this path the validator
    ///     that runs is the action's own, and a payment of zero — or of minus fifty, which would <em>lower</em>
    ///     what has been paid — is refused before any of this executes.
    /// </summary>
    [PositiveMoney]
    public required Money Amount { get; init; }

    [MaxLength(100)]
    public string? Reference { get; init; }

    public required PaymentMethod Method { get; init; }

    public override Task<Result<InvoiceDto, IError>> Execute(CancellationToken ct = default)
    {
        // ⚠️ Not `ValidateLoaded()`, although this is a rule about the loaded row: that hook returns a
        // ValidationError, which is a 422, and a state that refuses the operation is a 409 with a code the
        // client keys on. The same deviation, for the same reason, as IssueInvoiceAction.
        if (_invoice.Status != InvoiceStatus.Issued)
            return Task.FromResult(Result<InvoiceDto, IError>.Failure(new InvoiceNotIssuedError()));

        // The invoice records it: what is paid and what was paid are one fact, and this is the only place
        // that writes both.
        var recorded = _invoice.RecordPayment(PaidOn, Amount, Reference, Method);

        return Task.FromResult(recorded.IsFailure
            ? Result<InvoiceDto, IError>.Failure(recorded.Error)
            : Result<InvoiceDto, IError>.Success(InvoiceDto.FromEntity(_invoice)));
    }
}
