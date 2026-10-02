using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Internationalization.Diagnostics;

/// <summary>
///     Centralized diagnostics for Internationalization operations.
///     Provides a single ActivitySource and Meter for the entire I18N module.
/// </summary>
public static class I18NDiagnostics
{
    public const string SourceName = "Pragmatic.Internationalization";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    // Metrics
    public static readonly Counter<long> KeyLookups = Meter.CreateCounter<long>(
        "pragmatic.i18n.key_lookups",
        description: "Total number of translation key lookups");

    public static readonly Counter<long> MissingKeys = Meter.CreateCounter<long>(
        "pragmatic.i18n.missing_keys",
        description: "Number of translation keys that were not found");

    #region Activity Names

    public const string ResolveConfiguration = "I18N.ResolveConfiguration";
    public const string GetProviderConfiguration = "I18N.GetProviderConfiguration";
    public const string SetCulture = "I18N.SetCulture";
    public const string ValidateCulture = "I18N.ValidateCulture";
    public const string ParseAcceptLanguage = "I18N.ParseAcceptLanguage";

    #endregion

    #region Activity Creation Helpers

    /// <summary>
    ///     Starts an activity for configuration resolution.
    /// </summary>
    public static Activity? StartResolveConfiguration()
    {
        return ActivitySource.StartActivity(ResolveConfiguration);
    }

    /// <summary>
    ///     Starts an activity for provider configuration retrieval.
    /// </summary>
    public static Activity? StartGetProviderConfiguration(string providerName, int priority)
    {
        var activity = ActivitySource.StartActivity(GetProviderConfiguration);
        activity?.SetTag(I18NTags.ProviderName, providerName);
        activity?.SetTag(I18NTags.ProviderPriority, priority);
        return activity;
    }

    /// <summary>
    ///     Starts an activity for setting culture.
    /// </summary>
    public static Activity? StartSetCulture(string culture)
    {
        var activity = ActivitySource.StartActivity(SetCulture);
        activity?.SetTag(I18NTags.UICulture, culture);
        return activity;
    }

    /// <summary>
    ///     Starts an activity for culture validation.
    /// </summary>
    public static Activity? StartValidateCulture(string requestedCulture)
    {
        var activity = ActivitySource.StartActivity(ValidateCulture);
        activity?.SetTag(I18NTags.RequestedCulture, requestedCulture);
        return activity;
    }

    /// <summary>
    ///     Starts an activity for Accept-Language parsing.
    /// </summary>
    public static Activity? StartParseAcceptLanguage(string? headerValue)
    {
        var activity = ActivitySource.StartActivity(ParseAcceptLanguage);
        activity?.SetTag(I18NTags.AcceptLanguageHeader, headerValue ?? "(null)");
        return activity;
    }

    #endregion

    #region Activity Extension Methods

    extension(Activity? activity)
    {
        /// <summary>
        ///     Sets the resolved configuration on an activity.
        /// </summary>
        public Activity? SetResolvedConfiguration(string? uiCulture,
            string? dataCulture,
            string? currency)
        {
            activity?.SetTag(I18NTags.UICulture, uiCulture);
            activity?.SetTag(I18NTags.DataCulture, dataCulture);
            activity?.SetTag(I18NTags.Currency, currency);
            return activity;
        }

        /// <summary>
        ///     Sets the validation result on an activity.
        /// </summary>
        public Activity? SetValidationResult(bool isValid, string? resolvedCulture = null)
        {
            activity?.SetTag(I18NTags.ValidationResult, isValid ? "valid" : "invalid");
            if (resolvedCulture is not null)
                activity?.SetTag(I18NTags.ResolvedCulture, resolvedCulture);
            return activity;
        }
    }

    #endregion
}
