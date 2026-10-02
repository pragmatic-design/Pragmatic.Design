namespace Invoicing.Billing.Errors;

/// <summary>
///     Only a draft can be changed: an issued invoice is a document, and a void one is history.
/// </summary>
public sealed partial record InvoiceNotDraftError : Error
{
    public override string Code => "INVOICE_NOT_DRAFT";
    public override int StatusCode => 409;
}
