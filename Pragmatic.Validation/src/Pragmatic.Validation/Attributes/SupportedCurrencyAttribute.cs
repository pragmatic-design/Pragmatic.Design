using Pragmatic.Internationalization.Types;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Restricts a <see cref="Money" /> or <see cref="CurrencyCode" /> to a whitelist of currencies.
/// </summary>
/// <remarks>
///     <para>
///         Null passes: use <c>[Required]</c> for that. A value that is neither a <see cref="Money" />
///         nor a <see cref="CurrencyCode" /> is refused — there is no currency in it to check, and
///         accepting it would make the declaration look enforced where it is not.
///     </para>
///     <para>
///         ⚠️ The whitelist cannot be expressed with <c>[OneOf]</c>: that compares the whole value, and
///         a <see cref="Money" /> is an amount together with its currency. This attribute reaches into
///         the currency, which is why it exists as its own rule rather than as a configuration of a
///         generic one.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreatePaymentRequest
/// {
///     [Required]
///     [SupportedCurrency("USD", "EUR", "GBP")]
///     public Money Amount { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SupportedCurrencyAttribute : ValidationAttribute
{
    private readonly HashSet<string> _supportedCurrencies;

    /// <summary>
    ///     Creates the rule from the ISO 4217 codes that are allowed.
    /// </summary>
    /// <param name="currencies">The allowed currency codes.</param>
    public SupportedCurrencyAttribute(params string[] currencies)
    {
        Ensure.Ensure.ThrowIfNull(currencies);
        _supportedCurrencies = new HashSet<string>(currencies, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The allowed currency codes.
    /// </summary>
    public IReadOnlyCollection<string> SupportedCurrencies => _supportedCurrencies;

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.supportedcurrency";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var currencyCode = value switch
        {
            Money money => money.Currency.Code,
            CurrencyCode currency => currency.Code,
            _ => null
        };

        return currencyCode is not null && _supportedCurrencies.Contains(currencyCode);
    }
}
