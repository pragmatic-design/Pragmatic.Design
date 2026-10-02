using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.AspNetCore.Result;
using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;
using Pragmatic.Result.AspNetCore;
using Pragmatic.Result;

namespace Pragmatic.Internationalization.AspNetCore.Extensions;

/// <summary>
///     Builder for configuring additional I18N options after calling
///     <c>AddPragmaticInternationalization()</c>.
/// </summary>
public sealed class I18NBuilder
{
    private readonly IServiceCollection _services;

    internal I18NBuilder(IServiceCollection services)
    {
        _services = services;

        // Here and not only on the first AddLocalizationProvider: an application that declares no
        // translation source still resolves an IStringLocalizer, and reads every key as itself — the
        // contract for a missing translation, and the same thing it would read from an empty file.
        // A type that exists only once somebody remembered a builder call is the shape that made
        // Pragmatic.Documents.Templating.I18N unreachable.
        EnsureCompositeRegistered();
    }

    /// <summary>
    ///     Gets the service collection.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    ///     Sets the default UI culture.
    /// </summary>
    /// <param name="culture">The default UI culture.</param>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder DefaultCulture(CultureCode culture)
    {
        _services.Configure<I18NOptions>(options => options.DefaultUICulture = culture);
        return this;
    }

    /// <summary>
    ///     Sets the supported cultures.
    /// </summary>
    /// <param name="cultures">The supported cultures.</param>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder Support(params CultureCode[] cultures)
    {
        _services.Configure<I18NOptions>(options => options.SupportedCultures = cultures);
        return this;
    }

    /// <summary>
    ///     Registers a strongly-typed culture scope.
    /// </summary>
    /// <typeparam name="TScope">The scope type implementing <see cref="ICultureScope"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder AddScope<TScope>() where TScope : ICultureScope
    {
        _services.Configure<I18NOptions>(options =>
        {
            options.CustomScopes ??= new Dictionary<string, CultureCode>();
            // Only set default if not already configured
            if (!options.CustomScopes.ContainsKey(TScope.Name))
                options.CustomScopes[TScope.Name] = TScope.DefaultCulture;
        });
        return this;
    }

    /// <summary>
    ///     Adds a configuration provider to the I18N system.
    /// </summary>
    /// <typeparam name="TProvider">The provider type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder AddConfigProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>() where TProvider : class, II18NConfigProvider
    {
        _services.AddScoped<II18NConfigProvider, TProvider>();
        return this;
    }

    /// <summary>
    ///     Adds an already-built configuration provider instance to the I18N system.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>The instance is shared by every request.</b> An object that already exists cannot be
    ///     scoped — registering it means one instance for the lifetime of the process — so a provider
    ///     passed here must not read per-request or per-user state. One that does becomes a captive
    ///     dependency: whichever request touches it first decides the configuration everyone else gets,
    ///     and nothing fails to say so.
    ///     <para>
    ///         For a provider that depends on the current request, current user or current tenant, use
    ///         <see cref="AddConfigProvider{TProvider}()" /> or the factory overload — both register
    ///         scoped. The generated <c>UseUserCulture()</c> takes the scoped path for this reason.
    ///     </para>
    /// </remarks>
    public I18NBuilder AddConfigProvider(II18NConfigProvider provider)
    {
        Ensure.Ensure.ThrowIfNull(provider);
        _services.AddSingleton(provider);
        return this;
    }

    /// <summary>
    ///     Adds a configuration provider using a factory function.
    /// </summary>
    public I18NBuilder AddConfigProvider(Func<IServiceProvider, II18NConfigProvider> factory)
    {
        Ensure.Ensure.ThrowIfNull(factory);
        _services.AddScoped(factory);
        return this;
    }

    /// <summary>
    ///     Registers a localization provider for translation resolution.
    ///     Multiple providers can be registered — they are composed via <see cref="CompositeLocalizationProvider"/>
    ///     with priority-based lookup (higher priority wins).
    /// </summary>
    /// <remarks>
    ///     <b>Ask for <see cref="IStringLocalizer"/>.</b> It is registered over the composite, so it
    ///     reads every source registered here, and it is what the rest of the framework takes — the
    ///     <c>{{t:…}}</c> expressions of <c>Pragmatic.Documents.Templating.I18N</c> among them.
    ///     <see cref="ILocalizationProvider"/> and <see cref="CompositeLocalizationProvider"/> both
    ///     resolve to the same set of sources; the interface is the raw seam, the localizer adds the
    ///     culture fallback chain and the interpolation.
    /// </remarks>
    /// <typeparam name="TProvider">The provider type implementing <see cref="ILocalizationProvider"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder AddLocalizationProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>() where TProvider : class, ILocalizationProvider
    {
        _services.AddSingleton<ILocalizationProvider, TProvider>();
        EnsureCompositeRegistered();
        return this;
    }

    /// <summary>
    ///     Registers a localization provider instance.
    /// </summary>
    public I18NBuilder AddLocalizationProvider(ILocalizationProvider provider)
    {
        Ensure.Ensure.ThrowIfNull(provider);
        _services.AddSingleton(provider);
        EnsureCompositeRegistered();
        return this;
    }

    /// <summary>
    ///     Adds a JSON file-based localization provider that loads translations
    ///     from the specified directory (default: "translations").
    /// </summary>
    /// <remarks>
    ///     Called more than once — one directory per module is the usual shape — every directory is
    ///     read. <b>Ask for <see cref="IStringLocalizer"/>:</b> it is registered over the composite of
    ///     all of them. Asking for <see cref="ILocalizationProvider"/> resolves to the same set.
    /// </remarks>
    /// <param name="basePath">Path to the translations directory. Null uses the default from I18NOptions.</param>
    /// <param name="watchForChanges">Enable hot reload when files change.</param>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder AddJsonTranslations(string? basePath = null, bool watchForChanges = false)
    {
        _services.AddSingleton<ILocalizationProvider>(sp =>
        {
            var options = sp.GetService<Microsoft.Extensions.Options.IOptions<I18NOptions>>();
            var path = basePath ?? options?.Value.TranslationsPath ?? "translations";
            var logger = sp.GetService<ILogger<JsonLocalizationProvider>>();
            var provider = new JsonLocalizationProvider(path, watchForChanges, logger);
            ReportCulturesWithNoTranslations(provider, options?.Value, path, logger);
            return provider;
        });
        EnsureCompositeRegistered();
        return this;
    }

    /// <summary>
    ///     Enables localized ProblemDetails responses by registering
    ///     <see cref="LocalizedErrorMessageResolver"/> as the <see cref="IErrorMessageResolver"/>.
    ///     Uses the error's MessageKey convention to resolve title and detail translations.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public I18NBuilder LocalizeProblemDetails()
    {
        EnsureCompositeRegistered();
        // Replace the default NullErrorMessageResolver with our localized one
        _services.AddSingleton<IErrorMessageResolver, LocalizedErrorMessageResolver>();
        return this;
    }

    /// <summary>
    ///     Says, once, which declared cultures nothing translates.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Noticed per lookup, a missing translation file is a <c>Debug</c> line per request,
    ///         below every default log level, and a caller reading the default language forever. The
    ///         moment it is cheap to notice is the moment the directory is read.
    ///     </para>
    ///     <para>
    ///         A culture counts as translated when a file exists for it <em>or for any of its
    ///         parents</em>, which is the chain <c>JsonLocalizationProvider</c> walks: without that,
    ///         every application shipping one file per language would be warned about every region it
    ///         supports, and a warning everybody ignores is worse than none.
    ///     </para>
    ///     <para>
    ///         A warning and not a throw: a host that supports four languages and ships three is wrong
    ///         about one of them, not unable to start.
    ///     </para>
    /// </remarks>
    private static void ReportCulturesWithNoTranslations(
        JsonLocalizationProvider provider,
        I18NOptions? options,
        string path,
        ILogger? logger)
    {
        if (logger is null || options is null)
            return;

        var loaded = new HashSet<string>(provider.SupportedCultures, StringComparer.OrdinalIgnoreCase);
        if (loaded.Count == 0)
            return;

        var declared = new List<CultureCode>();
        if (options.SupportedCultures is not null)
            declared.AddRange(options.SupportedCultures);
        if (options.DefaultUICulture is { } defaultCulture)
            declared.Add(defaultCulture);

        foreach (var culture in declared.Select(c => c.Code).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ResolvesFrom(loaded, culture))
                continue;

            LogCultureHasNoTranslations(logger, culture, path, null);
        }
    }

    /// <summary>Whether a file exists for the culture or for any of its parents.</summary>
    private static bool ResolvesFrom(HashSet<string> loaded, string culture)
    {
        var candidate = culture;
        while (candidate.Length > 0)
        {
            if (loaded.Contains(candidate))
                return true;

            var lastSubtag = candidate.LastIndexOf('-');
            if (lastSubtag <= 0)
                return false;

            candidate = candidate.Substring(0, lastSubtag);
        }

        return false;
    }

    private static readonly Action<ILogger, string, string, Exception?> LogCultureHasNoTranslations =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1810, nameof(LogCultureHasNoTranslations)),
            "Culture '{Culture}' is declared as supported and no translation file in '{Path}' covers it, "
            + "so every lookup in that culture falls back to the default text.");

    /// <summary>
    ///     Registers the three things an application needs and would otherwise write itself: the
    ///     composite, the localizer over it, and the answer the interface gives.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Called after every source registration, because the gateway has to stay the <em>last</em>
    ///         <see cref="ILocalizationProvider" /> descriptor — that is what <c>GetRequiredService</c>
    ///         returns — and a source added afterwards would otherwise take its place and answer for all
    ///         of them.
    ///     </para>
    ///     <para>
    ///         ⚠️ The composite must not collect the gateway. It resolves back to the composite, so a
    ///         composite holding one would descend into itself on every lookup.
    ///     </para>
    ///     <para>
    ///         Everything here is <c>TryAdd</c> or repositioning: an application that has already
    ///         registered its own <see cref="IStringLocalizer" /> keeps it.
    ///     </para>
    /// </remarks>
    private void EnsureCompositeRegistered()
    {
        _services.TryAddSingleton<CompositeLocalizationProvider>(sp =>
            new CompositeLocalizationProvider(
                sp.GetServices<ILocalizationProvider>().Where(p => p is not LocalizationProviderGateway)));

        _services.TryAddSingleton<IStringLocalizer>(sp => new StringLocalizer(
            sp.GetRequiredService<CompositeLocalizationProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<I18NOptions>>().Value));

        MoveGatewayLast();
    }

    /// <summary>Re-registers the gateway at the end, so it is the one the interface resolves to.</summary>
    /// <remarks>
    ///     ⚠️ Last among what this builder registers. An application that adds an
    ///     <see cref="ILocalizationProvider" /> straight onto the service collection after its last
    ///     builder call takes the final slot back, and the interface answers with that one provider
    ///     again — the composite still collects it, so <see cref="IStringLocalizer" /> and
    ///     <see cref="CompositeLocalizationProvider" /> are unaffected. Register sources through the
    ///     builder.
    /// </remarks>
    private void MoveGatewayLast()
    {
        for (var i = _services.Count - 1; i >= 0; i--)
            if (_services[i].ServiceType == typeof(ILocalizationProvider)
                && _services[i].ImplementationType == typeof(LocalizationProviderGateway))
                _services.RemoveAt(i);

        _services.AddSingleton<ILocalizationProvider, LocalizationProviderGateway>();
    }
}
