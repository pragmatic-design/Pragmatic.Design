using System.Collections.Frozen;
using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Context;

public sealed partial class I18NContext
{
    // ══════════════════════════════════════════════════════════════
    // Static Setters
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Sets the current context from a resolved configuration.
    /// </summary>
    public static void SetFromConfig(I18NConfig config)
    {
        Current = FromConfig(config);
        SyncThreadCulture();
    }

    /// <summary>
    ///     Sets the current UI culture.
    /// </summary>
    /// <remarks>
    ///     If <see cref="SyncScopes"/> is true, also sets the Data culture.
    ///     Syncs with <see cref="Thread.CurrentUICulture"/>.
    /// </remarks>
    public static void SetCulture(CultureCode culture)
    {
        var current = Current;
        var dataCulture = current.SyncScopes ? culture : current.DataCulture;

        Current = new I18NContext(
            uiCulture: culture,
            dataCulture: dataCulture,
            preferredCurrency: current.PreferredCurrency,
            syncScopes: current.SyncScopes,
            customScopes: current._customScopes);

        SyncThreadCulture();
    }

    /// <summary>
    ///     Sets the current UI culture (string overload).
    /// </summary>
    public static void SetCulture(string culture)
    {
        SetCulture(Types.CultureCode.FromString(culture));
    }

    /// <summary>
    ///     Sets the current UI culture (CultureInfo overload).
    /// </summary>
    public static void SetCulture(System.Globalization.CultureInfo culture)
    {
        ThrowIfNull(culture);
        SetCulture(Types.CultureCode.FromCultureInfo(culture));
    }

    /// <summary>
    ///     Sets the current Data culture.
    /// </summary>
    /// <remarks>
    ///     Syncs with <see cref="Thread.CurrentCulture"/>.
    ///     Note: If <see cref="SyncScopes"/> is true, this is a no-op (Data follows UI).
    /// </remarks>
    public static void SetDataCulture(CultureCode culture)
    {
        var current = Current;

        // If SyncScopes, Data always follows UI
        if (current.SyncScopes)
            return;

        Current = new I18NContext(
            uiCulture: current.UICulture,
            dataCulture: culture,
            preferredCurrency: current.PreferredCurrency,
            syncScopes: current.SyncScopes,
            customScopes: current._customScopes);

        SyncThreadCulture();
    }

    /// <summary>
    ///     Sets the preferred currency.
    /// </summary>
    public static void SetPreferredCurrency(CurrencyCode? currency)
    {
        var current = Current;

        Current = new I18NContext(
            uiCulture: current.UICulture,
            dataCulture: current.DataCulture,
            preferredCurrency: currency,
            syncScopes: current.SyncScopes,
            customScopes: current._customScopes);
    }

    /// <summary>
    ///     Sets a custom scope's culture.
    /// </summary>
    public static void SetScope(string scopeName, CultureCode culture)
    {
        ThrowIfNullOrWhiteSpace(scopeName, nameof(scopeName));
        var current = Current;

        var scopes = current._customScopes.ToDictionary(x => x.Key, x => x.Value);
        scopes[scopeName] = culture;

        Current = new I18NContext(
            uiCulture: current.UICulture,
            dataCulture: current.DataCulture,
            preferredCurrency: current.PreferredCurrency,
            syncScopes: current.SyncScopes,
            customScopes: scopes.ToFrozenDictionary());
    }

    /// <summary>
    ///     Sets a strongly-typed scope's culture.
    /// </summary>
    /// <typeparam name="TScope">The scope type implementing <see cref="ICultureScope"/>.</typeparam>
    /// <param name="culture">The culture to set for this scope.</param>
    public static void SetScope<TScope>(CultureCode culture) where TScope : ICultureScope
    {
        SetScope(TScope.Name, culture);
    }

    /// <summary>
    ///     Clears the current context, reverting to system defaults.
    /// </summary>
    public static void Clear()
    {
        SCurrent.Value = null;
    }

    /// <summary>
    ///     Captures the current context state for later restoration.
    ///     Used primarily for testing to ensure isolation between tests.
    /// </summary>
    public static I18NContextSnapshot Capture()
    {
        return new I18NContextSnapshot(
            SCurrent.Value,
            Thread.CurrentThread.CurrentUICulture,
            Thread.CurrentThread.CurrentCulture);
    }

    /// <summary>
    ///     Restores a previously captured context state.
    /// </summary>
    public static void Restore(I18NContextSnapshot snapshot)
    {
        SCurrent.Value = snapshot.Context;
        Thread.CurrentThread.CurrentUICulture = snapshot.ThreadUICulture;
        Thread.CurrentThread.CurrentCulture = snapshot.ThreadCulture;
    }
}
