using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Configuration bundle for internationalization settings.
/// </summary>
/// <remarks>
///     <para>
///         All properties are nullable. A null value means "this provider doesn't set this field,
///         defer to lower-priority providers".
///     </para>
///     <para>
///         The <see cref="I18NConfigResolver"/> merges configurations from all providers,
///         with higher-priority providers overriding lower-priority ones.
///     </para>
/// </remarks>
public sealed record I18NConfig
{
    #region Culture Defaults

    /// <summary>
    ///     Gets or sets the default UI culture.
    /// </summary>
    /// <remarks>
    ///     Used for display labels, messages, and user-facing content.
    ///     Syncs with <see cref="System.Threading.Thread.CurrentUICulture"/>.
    /// </remarks>
    public CultureCode? DefaultUICulture { get; init; }

    /// <summary>
    ///     Gets or sets the default data culture.
    /// </summary>
    /// <remarks>
    ///     Used for data storage, APIs, and serialization.
    ///     Syncs with <see cref="System.Threading.Thread.CurrentCulture"/>.
    ///     Typically set to a standard culture (e.g., en-US) for consistency.
    /// </remarks>
    public CultureCode? DefaultDataCulture { get; init; }

    /// <summary>
    ///     Gets or sets whether to synchronize UI and Data cultures.
    /// </summary>
    /// <remarks>
    ///     When true, setting the UI culture automatically sets the Data culture to the same value.
    ///     Useful for simple sites where regional differences don't matter.
    /// </remarks>
    public bool? SyncScopes { get; init; }

    #endregion

    #region Supported Cultures

    /// <summary>
    ///     Gets or sets the list of supported cultures.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If a requested culture is not in this list, the system falls back to
    ///         the default culture or the first supported culture.
    ///     </para>
    ///     <para>
    ///         Setting this to null means "defer to lower priority provider".
    ///         Setting to an empty list is invalid and will cause an error.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<CultureCode>? SupportedCultures { get; init; }

    #endregion

    #region Currency

    /// <summary>
    ///     Gets or sets the preferred currency.
    /// </summary>
    /// <remarks>
    ///     Overrides the default currency from the UI culture's country.
    ///     Useful when a user has a specific currency preference.
    /// </remarks>
    public CurrencyCode? PreferredCurrency { get; init; }

    #endregion

    #region Custom Scopes

    /// <summary>
    ///     Gets or sets custom scope defaults.
    /// </summary>
    /// <remarks>
    ///     Custom scopes allow different cultures for specific purposes
    ///     (e.g., "invoicing" scope with a different culture than UI).
    /// </remarks>
    public IReadOnlyDictionary<string, CultureCode>? CustomScopes { get; init; }

    #endregion

    #region Static Helpers

    /// <summary>
    ///     An empty configuration that defers all settings to other providers.
    /// </summary>
    public static I18NConfig Empty { get; } = new();

    #endregion
}
