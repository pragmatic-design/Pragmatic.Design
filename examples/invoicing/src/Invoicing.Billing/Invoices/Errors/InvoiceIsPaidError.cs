namespace Invoicing.Billing.Errors;

/// <summary>
///     A settled invoice is not voided.
/// </summary>
/// <remarks>
///     Money came in against it, so the document stands until somebody reverses the payment — which is an
///     operation of its own (<c>DELETE api/invoices/{id}/payments/{id}</c>), with its own trail. Voiding it
///     here would leave a payment recorded against a document that says it was never owed.
/// </remarks>
public sealed partial record InvoiceIsPaidError : Error
{
    public override string Code => "INVOICE_IS_PAID";
    public override int StatusCode => 409;
}
