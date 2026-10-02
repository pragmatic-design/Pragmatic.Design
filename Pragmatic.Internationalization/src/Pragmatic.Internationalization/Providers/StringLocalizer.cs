using System.Text.RegularExpressions;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Diagnostics;
using Pragmatic.Internationalization.Types;
using Pragmatic.Telemetry.Conventions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Default implementation of IStringLocalizer that uses localization providers.
/// </summary>
public sealed partial class StringLocalizer : IStringLocalizer
{

    private readonly I18NOptions _options;
    private readonly ILocalizationProvider _provider;
    // Null means "resolve lazily from I18NContext.Current at call time" (used when constructed
    // without an explicit culture so that scoped/singleton instances always see the current culture).
    private readonly string? _fixedCulture;

    /// <summary>
    ///     Creates a new StringLocalizer that resolves culture lazily from <see cref="I18NContext.Current"/>
    ///     on every call. This is the correct default for scoped/singleton DI registrations.
    /// </summary>
    public StringLocalizer(ILocalizationProvider provider, I18NOptions options)
    {
        ThrowIfNull(provider);
        ThrowIfNull(options);
        _provider = provider;
        _options = options;
        _fixedCulture = null; // lazy — resolved per-call from I18NContext
    }

    /// <summary>
    ///     Creates a new StringLocalizer for a specific culture.
    /// </summary>
    public StringLocalizer(ILocalizationProvider provider, I18NOptions options, string culture)
    {
        ThrowIfNull(provider);
        ThrowIfNull(options);
        ThrowIfNull(culture);
        _provider = provider;
        _options = options;
        _fixedCulture = culture;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Returns the fixed culture supplied at construction, or the current
    ///     <see cref="I18NContext.Current"/> culture when no fixed culture was given.
    /// </remarks>
    public string Culture => _fixedCulture ?? I18NContext.Current.CultureCode;

    /// <inheritdoc />
    public TranslationResult this[string key] => GetString(key);

    /// <inheritdoc />
    public TranslationResult this[string key, params object[] args] => GetString(key, args);

    /// <inheritdoc />
    public TranslationResult Plural(string key, int count)
    {
        return GetPluralString(key, count, []);
    }

    /// <inheritdoc />
    public TranslationResult Plural(string key, int count, params object[] args)
    {
        return GetPluralString(key, count, args);
    }

    /// <inheritdoc />
    public IStringLocalizer WithCulture(string culture)
    {
        return new StringLocalizer(_provider, _options, culture);
    }

    private TranslationResult GetString(string key, object[]? args = null)
    {
        using var activity = I18NDiagnostics.ActivitySource.StartActivity("Localization.GetString");
        activity?.SetTag(I18NTags.LocalizationKey, key);
        activity?.SetTag(I18NTags.LocalizationCulture, Culture);

        I18NDiagnostics.KeyLookups.Add(1, new KeyValuePair<string, object?>("culture", Culture));

        // Try direct match
        var value = _provider.GetString(key, Culture);
        var resolvedFromCulture = value is not null ? Culture : null;

        // Try fallback chain
        if (value is null)
            foreach (var fallback in _options.GetFallbacks(CultureCode.FromString(Culture)).Select(c => c.Code))
            {
                value = _provider.GetString(key, fallback);
                if (value is not null)
                {
                    resolvedFromCulture = fallback;
                    activity?.SetTag(I18NTags.FallbackUsed, true);
                    break;
                }
            }

        // Return missing if not found
        if (value is null)
        {
            I18NDiagnostics.MissingKeys.Add(1,
                new KeyValuePair<string, object?>("key", key),
                new KeyValuePair<string, object?>("culture", Culture));
            activity?.SetTag(I18NTags.Missing, true);
            return TranslationResult.Missing(key);
        }

        activity?.SetTag(I18NTags.ResolvedCulture, resolvedFromCulture);

        // Interpolate if we have args
        if (args is { Length: > 0 })
            value = InterpolatePositional(value, args);

        return TranslationResult.Found(key, value);
    }

    private TranslationResult GetPluralString(string key, int count, object[] additionalArgs)
    {
        using var activity = I18NDiagnostics.ActivitySource.StartActivity("Localization.GetPlural");
        activity?.SetTag(I18NTags.LocalizationKey, key);
        activity?.SetTag(I18NTags.LocalizationCulture, Culture);
        activity?.SetTag(I18NTags.Count, count);

        I18NDiagnostics.KeyLookups.Add(1, new KeyValuePair<string, object?>("culture", Culture));

        // Try to get plural forms
        var plural = _provider.GetPlural(key, Culture);

        // Try fallback chain
        if (plural is null)
            foreach (var fallback in _options.GetFallbacks(CultureCode.FromString(Culture)).Select(c => c.Code))
            {
                plural = _provider.GetPlural(key, fallback);
                if (plural is not null)
                {
                    activity?.SetTag(I18NTags.FallbackUsed, true);
                    break;
                }
            }

        // If no plural forms, try simple string with count — honoring the same
        // fallback chain that the plural-form branch (and GetString) use.
        if (plural is null)
        {
            var simpleValue = _provider.GetString(key, Culture);
            if (simpleValue is null)
                foreach (var fallback in _options.GetFallbacks(CultureCode.FromString(Culture)).Select(c => c.Code))
                {
                    simpleValue = _provider.GetString(key, fallback);
                    if (simpleValue is not null)
                    {
                        activity?.SetTag(I18NTags.FallbackUsed, true);
                        break;
                    }
                }

            if (simpleValue is not null)
            {
                simpleValue = simpleValue.Replace("{count}", count.ToString());
                if (additionalArgs.Length > 0)
                    simpleValue = InterpolatePositional(simpleValue, additionalArgs);
                return TranslationResult.Found(key, simpleValue);
            }

            I18NDiagnostics.MissingKeys.Add(1,
                new KeyValuePair<string, object?>("key", key),
                new KeyValuePair<string, object?>("culture", Culture));
            activity?.SetTag(I18NTags.Missing, true);
            return TranslationResult.Missing(key);
        }

        // Get the appropriate plural form
        var category = PluralRules.GetCategory(Culture, count);
        var template = plural[category];
        activity?.SetTag(I18NTags.PluralCategory, category.ToString());

        // Replace count placeholder
        var value = template.Replace("{count}", count.ToString());

        // Interpolate additional args
        if (additionalArgs.Length > 0)
            value = InterpolatePositional(value, additionalArgs);

        return TranslationResult.Found(key, value);
    }

    /// <summary>
    ///     Interpolates positional placeholders like {0}, {1}, etc.
    ///     Also handles named placeholders if the args are provided as tuples.
    /// </summary>
    private static string InterpolatePositional(string template, object[] args)
    {
        // Simple positional interpolation
        return PositionalPlaceholderRegex().Replace(template, match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var index) && index < args.Length)
                return args[index]?.ToString() ?? "";
            return match.Value;
        });
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PositionalPlaceholderRegex();
}