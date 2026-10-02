namespace Invoicing.Billing.Errors;

/// <summary>
///     Two invoices of the same company were issued at the same moment and both wanted the next number.
/// </summary>
/// <remarks>
///     A refusal the caller retries — never a number silently reused or skipped. The series row is
///     <c>[ConcurrencyAware]</c>, so one of the two loses the check and lands here.
/// </remarks>
public sealed partial record InvoiceNumberContendedError : Error
{
    public override string Code => "INVOICE_NUMBER_CONTENDED";
    public override int StatusCode => 409;
}
