namespace Invoicing.Billing.Errors;

/// <summary>
///     A payment in a currency the invoice was not issued in.
/// </summary>
/// <remarks>
///     Converting it is out of this example's scope, and this is what "out of scope" has to look like from
///     the outside: a refusal that names both currencies. Adding it to the invoice's euros would be wrong,
///     and letting <c>Money</c>'s own guard throw would answer 500 to a request that is merely mistaken.
/// </remarks>
public sealed partial record CurrencyMismatchError : Error
{
    public override string Code => "CURRENCY_MISMATCH";
    public override int StatusCode => 422;

    /// <summary>The currency the invoice was issued in.</summary>
    public required string Expected { get; init; }

    /// <summary>The currency the payment arrived in.</summary>
    public required string Offered { get; init; }

    /// <summary>Both currencies, by name, for <c>error.currency.mismatch.detail</c>.</summary>
    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["expected"] = Expected,
        ["offered"] = Offered
    };
}
