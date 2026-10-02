using System.Globalization;
using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Static facade for <see cref="I18NContext"/>. Provides shorter API for common operations.
///     Each method delegates 1:1 to I18NContext — maintained intentionally for DX despite duplication.
/// </summary>
/// <example>
/// <code>
/// // Get current cultures
/// var uiCulture = I18N.UI;          // CultureCode for display
/// var dataCulture = I18N.Data;      // CultureCode for storage/API
///
/// // Get current settings
/// var currency = I18N.Currency;     // Effective currency
///
/// // Create money with current currency
/// var price = I18N.CreateMoney(99.99m);   // Money in I18N.Currency
///
/// // Access custom scopes
/// var invoiceCulture = I18N.Scope["invoicing"];
///
/// // Execute with temporary culture
/// I18N.WithCulture(CultureCode.German, () => {
///     // Code runs with German culture
/// });
/// </code>
/// </example>
public static partial class I18N
{
    #region Current Context

    /// <summary>
    ///     Gets the current context.
    /// </summary>
    public static I18NContext Current => I18NContext.Current;

    #endregion

    #region Culture Shortcuts

    /// <summary>
    ///     Gets the primary culture (same as <see cref="UI"/>).
    /// </summary>
    public static CultureCode Culture => I18NContext.Current.UICulture;

    /// <summary>
    ///     Gets the UI culture for display.
    /// </summary>
    public static CultureCode UI => I18NContext.Current.UICulture;

    /// <summary>
    ///     Gets the Data culture for storage/APIs.
    /// </summary>
    public static CultureCode Data => I18NContext.Current.DataCulture;

    /// <summary>
    ///     Gets the culture code string (e.g., "en", "it-IT").
    /// </summary>
    public static string CultureCode => I18NContext.Current.CultureCode;

    /// <summary>
    ///     Gets the current CultureInfo for formatting.
    /// </summary>
    public static CultureInfo CultureInfo => I18NContext.Current.Culture;

    /// <summary>
    ///     Custom scope accessor.
    /// </summary>
    /// <example>
    /// <code>
    /// // Set a custom scope
    /// I18N.SetScope("invoicing", CultureCode.German);
    ///
    /// // Access via indexer
    /// var invoiceCulture = I18N.Scope["invoicing"];  // CultureCode.German
    /// </code>
    /// </example>
    public static ScopeAccessor Scope { get; } = new();

    #endregion

    #region Currency Shortcuts

    /// <summary>
    ///     Gets the effective currency (preferred if set, otherwise from UI culture).
    /// </summary>
    public static CurrencyCode Currency => I18NContext.Current.Currency;

    /// <summary>
    ///     Gets zero money in the current currency.
    /// </summary>
    public static Money Zero => Types.Money.Zero(Currency);

    /// <summary>
    ///     Creates money in the current currency.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>Money in the current currency.</returns>
    public static Money CreateMoney(decimal amount) => Types.Money.From(amount, Currency);

    #endregion

    #region Setters

    /// <summary>
    ///     Sets the current UI culture.
    /// </summary>
    /// <param name="culture">The culture to set.</param>
    public static void SetCulture(CultureCode culture)
    {
        I18NContext.SetCulture(culture);
    }

    /// <summary>
    ///     Sets the current UI culture (string overload).
    /// </summary>
    /// <param name="culture">The culture code string.</param>
    public static void SetCulture(string culture)
    {
        I18NContext.SetCulture(culture);
    }

    /// <summary>
    ///     Sets the current UI culture (CultureInfo overload).
    /// </summary>
    /// <param name="culture">The CultureInfo.</param>
    public static void SetCulture(CultureInfo culture)
    {
        I18NContext.SetCulture(culture);
    }

    /// <summary>
    ///     Sets the current Data culture.
    /// </summary>
    /// <param name="culture">The culture to set.</param>
    public static void SetDataCulture(CultureCode culture)
    {
        I18NContext.SetDataCulture(culture);
    }

    /// <summary>
    ///     Sets a custom scope's culture.
    /// </summary>
    /// <param name="scopeName">The scope name.</param>
    /// <param name="culture">The culture for the scope.</param>
    public static void SetScope(string scopeName, CultureCode culture)
    {
        I18NContext.SetScope(scopeName, culture);
    }

    /// <summary>
    ///     Sets a strongly-typed scope's culture.
    /// </summary>
    public static void SetScope<TScope>(CultureCode culture) where TScope : ICultureScope
    {
        I18NContext.SetScope<TScope>(culture);
    }

    /// <summary>
    ///     Gets a strongly-typed scope's culture.
    /// </summary>
    public static CultureCode GetScope<TScope>() where TScope : ICultureScope
    {
        return I18NContext.Current.GetScope<TScope>();
    }

    /// <summary>
    ///     Sets the preferred currency.
    /// </summary>
    /// <param name="currency">The preferred currency, or null to use the default.</param>
    public static void SetPreferredCurrency(CurrencyCode? currency)
    {
        I18NContext.SetPreferredCurrency(currency);
    }

    /// <summary>
    ///     Sets the current context from a resolved configuration.
    /// </summary>
    public static void SetFromConfig(I18NConfig config)
    {
        I18NContext.SetFromConfig(config);
    }

    /// <summary>
    ///     Clears the current context.
    /// </summary>
    public static void Clear()
    {
        I18NContext.Clear();
    }

    /// <summary>
    ///     Captures the current context state for later restoration.
    /// </summary>
    public static I18NContextSnapshot Capture()
    {
        return I18NContext.Capture();
    }

    /// <summary>
    ///     Restores a previously captured context state.
    /// </summary>
    public static void Restore(I18NContextSnapshot snapshot)
    {
        I18NContext.Restore(snapshot);
    }

    #endregion

    #region ScopeAccessor

    /// <summary>
    ///     Helper class to enable indexer syntax on static <see cref="I18N"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var invoiceCulture = I18N.Scope["invoicing"];
    /// </code>
    /// </example>
    public sealed class ScopeAccessor
    {
        /// <summary>
        ///     Gets the culture for the specified scope.
        /// </summary>
        /// <param name="scopeName">The scope name.</param>
        /// <returns>The culture for the scope, or UI culture if not set.</returns>
        public CultureCode this[string scopeName] => I18NContext.Current.GetScope(scopeName);
    }

    #endregion
}
