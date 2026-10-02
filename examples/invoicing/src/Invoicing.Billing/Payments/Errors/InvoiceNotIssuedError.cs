namespace Invoicing.Billing.Errors;

/// <summary>
///     Only an issued invoice can be paid: nobody was asked to pay a draft, and a void one is not owed.
/// </summary>
public sealed partial record InvoiceNotIssuedError : Error
{
    public override string Code => "INVOICE_NOT_ISSUED";
    public override int StatusCode => 409;
}
