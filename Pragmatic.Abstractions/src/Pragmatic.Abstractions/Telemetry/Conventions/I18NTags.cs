namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     OpenTelemetry semantic conventions for Internationalization operations.
///     Uses <c>pragmatic.i18n.*</c> prefix for framework-specific tags
///     and <c>pragmatic.localization.*</c> for string localization tags.
/// </summary>
public static class I18NTags
{
    /// <summary>The resolved UI culture (e.g., "en-US").</summary>
    public const string UICulture = "pragmatic.i18n.ui_culture";

    /// <summary>The resolved data culture for formatting.</summary>
    public const string DataCulture = "pragmatic.i18n.data_culture";

    /// <summary>The resolved currency code (e.g., "EUR").</summary>
    public const string Currency = "pragmatic.i18n.currency";

    /// <summary>The source that resolved the culture (e.g., "header", "cookie", "default").</summary>
    public const string Source = "pragmatic.i18n.source";

    /// <summary>The culture originally requested by the client.</summary>
    public const string RequestedCulture = "pragmatic.i18n.requested_culture";

    /// <summary>The culture actually used after negotiation/fallback.</summary>
    public const string ResolvedCulture = "pragmatic.i18n.resolved_culture";

    /// <summary>Culture validation result (e.g., "valid", "unsupported", "fallback").</summary>
    public const string ValidationResult = "pragmatic.i18n.validation_result";

    /// <summary>Name of the culture/localization provider.</summary>
    public const string ProviderName = "pragmatic.i18n.provider_name";

    /// <summary>Priority of the provider in the chain.</summary>
    public const string ProviderPriority = "pragmatic.i18n.provider_priority";

    /// <summary>The localization key being resolved.</summary>
    public const string LocalizationKey = "pragmatic.localization.key";

    /// <summary>The culture used for localization lookup.</summary>
    public const string LocalizationCulture = "pragmatic.localization.culture";

    /// <summary>Whether a fallback culture was used.</summary>
    public const string FallbackUsed = "pragmatic.localization.fallback_used";

    /// <summary>Whether the localization key was missing (not found).</summary>
    public const string Missing = "pragmatic.localization.missing";

    /// <summary>Number of localized strings in the operation.</summary>
    public const string Count = "pragmatic.localization.count";

    /// <summary>ICU plural category (e.g., "one", "few", "many", "other").</summary>
    public const string PluralCategory = "pragmatic.localization.plural_category";

    /// <summary>File path of the localization resource file.</summary>
    public const string FilePath = "pragmatic.localization.file_path";

    /// <summary>Total count of strings loaded from a resource file.</summary>
    public const string StringsCount = "pragmatic.localization.strings_count";

    /// <summary>Total count of plural forms loaded from a resource file.</summary>
    public const string PluralsCount = "pragmatic.localization.plurals_count";

    /// <summary>The Accept-Language header the culture was negotiated from.</summary>
    public const string AcceptLanguageHeader = "pragmatic.i18n.accept_language";
}
