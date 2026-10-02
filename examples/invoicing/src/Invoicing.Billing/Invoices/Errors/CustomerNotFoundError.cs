namespace Invoicing.Billing.Errors;

/// <summary>
///     The customer an invoice is drafted for does not exist in this company's register.
/// </summary>
/// <remarks>
///     404 and not 422: the id names nothing this caller can see — and a customer of another company is
///     exactly as absent as one that never existed, which is the answer tenancy is supposed to give.
/// </remarks>
public sealed partial record CustomerNotFoundError : Error
{
    public override string Code => "CUSTOMER_NOT_FOUND";
    public override int StatusCode => 404;
}
