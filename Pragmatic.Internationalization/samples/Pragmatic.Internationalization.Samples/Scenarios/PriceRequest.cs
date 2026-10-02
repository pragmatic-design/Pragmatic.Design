using Pragmatic.Internationalization.Types;
using Pragmatic.Validation.Attributes;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     A request with monetary fields, validated by the three money rules.
/// </summary>
/// <remarks>
///     ⚠️ <c>partial</c> is the whole wiring: the generator writes <c>Validate()</c> into this type at
///     compile time. Nothing scans the model at runtime and there is no validator to register.
/// </remarks>
internal sealed partial record PriceRequest
{
    /// <summary>The price of one unit, which has to be more than nothing.</summary>
    [PositiveMoney]
    public Money UnitPrice { get; init; }

    /// <summary>A discount, which may be zero but not negative.</summary>
    [NonNegativeMoney]
    public Money Discount { get; init; }

    /// <summary>The currency the payment settles in, restricted to what this sample accepts.</summary>
    [SupportedCurrency("USD", "EUR")]
    public CurrencyCode SettlementCurrency { get; init; }
}
