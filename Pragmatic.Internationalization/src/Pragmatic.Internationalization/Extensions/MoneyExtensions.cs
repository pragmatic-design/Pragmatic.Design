using System.Globalization;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Extensions;

/// <summary>
///     Extension methods for <see cref="Money" /> formatting.
/// </summary>
public static class MoneyExtensions
{
    /// <param name="money">The money value to format.</param>
    extension(Money money)
    {
        /// <summary>
        ///     Formats the money value using the current globalization context or culture.
        /// </summary>
        /// <returns>A culture-appropriate string representation.</returns>
        /// <remarks>
        ///     The culture is determined in this order:
        ///     <list type="number">
        ///         <item><see cref="I18NContext.Current" /> if set</item>
        ///         <item><see cref="CultureInfo.CurrentCulture" /> as fallback</item>
        ///     </list>
        /// </remarks>
        public string Format()
        {
            return money.Format(I18NContext.EffectiveCulture);
        }

        /// <summary>
        ///     Formats the money value using the specified culture.
        /// </summary>
        /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
        /// <returns>A culture-appropriate string representation.</returns>
        public string Format(string cultureName)
        {
            return money.Format(CultureInfo.GetCultureInfo(cultureName));
        }

        /// <summary>
        ///     Returns a new Money with the amount rounded to the currency's minor units.
        /// </summary>
        /// <returns>A new Money with rounded amount.</returns>
        public Money RoundToCurrency()
        {
            return money.RoundToMinorUnit();
        }
    }

    /// <summary>
    ///     Converts a nullable Money to its formatted string or returns a default value.
    /// </summary>
    /// <param name="money">The nullable money value.</param>
    /// <param name="defaultValue">The value to return if money is null.</param>
    /// <returns>The formatted string or default value.</returns>
    public static string FormatOrDefault(this Money? money, string defaultValue = "-")
    {
        return money?.Format() ?? defaultValue;
    }

    /// <summary>
    ///     Creates a <see cref="Money" /> value from this decimal using the current context's currency.
    /// </summary>
    /// <param name="amount">The decimal amount.</param>
    /// <returns>A Money value in the current I18N currency.</returns>
    /// <example>
    ///     <code>
    /// I18NContext.SetCulture(CultureCode.Italian);
    /// var price = 99.99m.ToMoney();  // Money in EUR
    /// </code>
    /// </example>
    public static Money ToMoney(this decimal amount)
    {
        return Money.From(amount, I18NContext.Current.Currency);
    }

    /// <summary>
    ///     Creates a <see cref="Money" /> value from this integer using the current context's currency.
    /// </summary>
    /// <param name="amount">The integer amount.</param>
    /// <returns>A Money value in the current I18N currency.</returns>
    public static Money ToMoney(this int amount)
    {
        return Money.From(amount, I18NContext.Current.Currency);
    }

    /// <summary>
    ///     Creates a <see cref="Money" /> value from this decimal using the specified currency.
    /// </summary>
    /// <param name="amount">The decimal amount.</param>
    /// <param name="currency">The currency code.</param>
    /// <returns>A Money value in the specified currency.</returns>
    public static Money ToMoney(this decimal amount, CurrencyCode currency)
    {
        return Money.From(amount, currency);
    }

    /// <summary>
    ///     Creates a <see cref="Money" /> value from this integer using the specified currency.
    /// </summary>
    /// <param name="amount">The integer amount.</param>
    /// <param name="currency">The currency code.</param>
    /// <returns>A Money value in the specified currency.</returns>
    public static Money ToMoney(this int amount, CurrencyCode currency)
    {
        return Money.From(amount, currency);
    }

    /// <summary>
    ///     Formats the money value using the specified culture code.
    /// </summary>
    /// <param name="money">The money value to format.</param>
    /// <param name="culture">The culture code.</param>
    /// <returns>A culture-appropriate string representation.</returns>
    public static string Format(this Money money, CultureCode culture)
    {
        return money.Format(culture.ToCultureInfo());
    }
}