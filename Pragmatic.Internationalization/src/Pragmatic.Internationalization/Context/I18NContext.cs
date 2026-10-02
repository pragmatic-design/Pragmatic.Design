using System.Collections.Frozen;
using System.Globalization;
using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Unified internationalization context providing both globalization (formatting) and localization (translations).
///     Thread-safe via AsyncLocal storage.
/// </summary>
/// <remarks>
///     <para>
///         This context combines both culture-aware formatting (numbers, dates, money) and
///         translation/localization features into a single, easy-to-use API.
///     </para>
///     <para>
///         Supports multiple culture scopes: UI (for display), Data (for storage/APIs),
///         and custom scopes (for specific purposes like invoicing).
///     </para>
///     <para>
///         In ASP.NET Core, the context is typically set by middleware based on request headers
///         or user preferences. In other scenarios (console apps, tests), use <see cref="SetCulture(CultureCode)"/>
///         or configure via <see cref="SetFromConfig"/>.
///     </para>
/// </remarks>
public sealed partial class I18NContext : IGlobalizationContext
{
    private static readonly AsyncLocal<I18NContext?> SCurrent = new();

    private readonly FrozenDictionary<string, CultureCode> _customScopes;

    private I18NContext(
        CultureCode uiCulture,
        CultureCode dataCulture,
        CurrencyCode? preferredCurrency,
        bool syncScopes,
        FrozenDictionary<string, CultureCode>? customScopes)
    {
        UICulture = uiCulture;
        DataCulture = dataCulture;
        PreferredCurrency = preferredCurrency;
        SyncScopes = syncScopes;
        _customScopes = customScopes ?? FrozenDictionary<string, CultureCode>.Empty;
    }

    // ══════════════════════════════════════════════════════════════
    // Current Context
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets or sets the current internationalization context.
    ///     Falls back to a default context using CultureInfo.CurrentCulture if not set.
    /// </summary>
    public static I18NContext Current
    {
        get => SCurrent.Value ?? FromCurrentThread();
        set => SCurrent.Value = value;
    }

    /// <summary>
    ///     Gets the effective culture - from context if available, otherwise CurrentCulture.
    ///     Used by formatting extensions.
    /// </summary>
    internal static CultureInfo EffectiveCulture
        => SCurrent.Value?.UICulture.ToCultureInfo() ?? CultureInfo.CurrentCulture;

    // ══════════════════════════════════════════════════════════════
    // Culture Properties (Multi-Scope)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets the UI culture for display (labels, messages, user-facing content).
    /// </summary>
    public CultureCode UICulture { get; }

    /// <summary>
    ///     Gets the Data culture for storage and APIs.
    /// </summary>
    public CultureCode DataCulture { get; }

    /// <summary>
    ///     Gets whether UI and Data scopes are synchronized.
    /// </summary>
    public bool SyncScopes { get; }

    /// <summary>
    ///     Gets a custom scope's culture.
    /// </summary>
    /// <param name="scopeName">The scope name (e.g., "invoicing").</param>
    /// <returns>The culture for the scope, or <see cref="UICulture"/> if not set.</returns>
    public CultureCode GetScope(string scopeName)
    {
        ThrowIfNullOrWhiteSpace(scopeName, nameof(scopeName));
        return _customScopes.TryGetValue(scopeName, out var culture) ? culture : UICulture;
    }

    /// <summary>
    ///     Gets a strongly-typed scope's culture.
    /// </summary>
    /// <typeparam name="TScope">The scope type implementing <see cref="ICultureScope"/>.</typeparam>
    /// <returns>The culture for the scope, or <typeparamref name="TScope"/>.DefaultCulture if not set.</returns>
    public CultureCode GetScope<TScope>() where TScope : ICultureScope
    {
        return _customScopes.TryGetValue(TScope.Name, out var culture)
            ? culture
            : TScope.DefaultCulture;
    }

    // ══════════════════════════════════════════════════════════════
    // Backward Compatibility Aliases
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets the primary culture (alias for <see cref="UICulture"/>).
    /// </summary>
    public CultureInfo Culture => UICulture.ToCultureInfo();

    /// <summary>
    ///     Gets the culture code string (e.g., "en", "it-IT").
    /// </summary>
    public string CultureCode => UICulture.Code;

    // ══════════════════════════════════════════════════════════════
    // Currency
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets the default currency based on the UI culture's country.
    /// </summary>
    public CurrencyCode DefaultCurrency => UICulture.DefaultCurrency;

    /// <summary>
    ///     Gets the preferred currency (can override <see cref="DefaultCurrency"/>).
    /// </summary>
    public CurrencyCode? PreferredCurrency { get; }

    /// <summary>
    ///     Gets the effective currency (preferred if set, otherwise default).
    /// </summary>
    public CurrencyCode Currency => PreferredCurrency ?? DefaultCurrency;

    /// <inheritdoc />
    string? IGlobalizationContext.CurrencyCode => Currency.Code;

    // ══════════════════════════════════════════════════════════════
    // Factory Methods
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Creates a new context from a resolved configuration.
    /// </summary>
    public static I18NContext FromConfig(I18NConfig config)
    {
        ThrowIfNull(config);

        var uiCulture = config.DefaultUICulture
            ?? throw I18NConfigurationException.NoUICultureConfigured();

        var dataCulture = config.DefaultDataCulture ?? uiCulture;
        var syncScopes = config.SyncScopes ?? false;

        return new I18NContext(
            uiCulture: uiCulture,
            dataCulture: syncScopes ? uiCulture : dataCulture,
            preferredCurrency: config.PreferredCurrency,
            syncScopes: syncScopes,
            customScopes: config.CustomScopes?.ToFrozenDictionary());
    }

    /// <summary>
    ///     Creates a new context for the specified UI culture.
    /// </summary>
    public static I18NContext ForCulture(CultureCode culture)
    {
        return new I18NContext(
            uiCulture: culture,
            dataCulture: culture,
            preferredCurrency: null,
            syncScopes: true,
            customScopes: null);
    }

    /// <summary>
    ///     Creates a new context for the specified culture string.
    /// </summary>
    public static I18NContext ForCulture(string culture)
    {
        return ForCulture(Types.CultureCode.FromString(culture));
    }

    /// <summary>
    ///     Creates a new context for the specified CultureInfo.
    /// </summary>
    public static I18NContext ForCulture(CultureInfo culture)
    {
        ThrowIfNull(culture);
        return ForCulture(Types.CultureCode.FromCultureInfo(culture));
    }

    private static I18NContext FromCurrentThread()
    {
        var culture = Types.CultureCode.FromCultureInfo(CultureInfo.CurrentCulture);
        return ForCulture(culture);
    }

    // ══════════════════════════════════════════════════════════════
    // Thread Culture Synchronization
    // ══════════════════════════════════════════════════════════════

    private static void SyncThreadCulture()
    {
        var current = SCurrent.Value;
        if (current is null)
            return;

        Thread.CurrentThread.CurrentUICulture = current.UICulture.ToCultureInfo();
        Thread.CurrentThread.CurrentCulture = current.DataCulture.ToCultureInfo();
    }
}
