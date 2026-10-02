namespace Invoicing.Billing.Errors;

/// <summary>
///     More than the invoice charges. 422 and not 409: the request is well formed and the invoice is in a
///     state that accepts payments — it is this amount that cannot be accepted.
/// </summary>
/// <remarks>
///     It carries what is still owed, because that is the number whoever typed the wrong one needs. The two
///     properties are written into the problem details as extensions by the generated
///     <c>WriteExtensions</c>, so the client reads them without parsing a sentence.
/// </remarks>
public sealed partial record OverpaymentError : Error
{
    public override string Code => "OVERPAYMENT";
    public override int StatusCode => 422;

    /// <summary>What is still owed before this payment.</summary>
    public required decimal AmountDue { get; init; }

    public required string Currency { get; init; }

    /// <summary>
    ///     The two numbers, by name, for the localized message: <c>error.overpayment.detail</c> reads
    ///     <c>{amountDue}</c> and <c>{currency}</c>.
    /// </summary>
    /// <remarks>
    ///     Named here and positional nowhere: the problem-details localizer interpolates by name from this
    ///     dictionary, while <c>IStringLocalizer["key", args]</c> — what the document and the reminder use
    ///     — interpolates <c>{0}</c>, <c>{1}</c>. Two mechanisms, and the JSON has to match the one that
    ///     will read it.
    /// </remarks>
    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["amountDue"] = AmountDue,
        ["currency"] = Currency
    };
}
