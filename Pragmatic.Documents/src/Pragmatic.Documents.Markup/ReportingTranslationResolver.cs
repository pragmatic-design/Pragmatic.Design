using System.Globalization;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Resolves <c>t:</c> through the localizer and records a key it has no translation for as a
///     <see cref="TemplateWarning" />.
/// </summary>
/// <remarks>
///     A missing key renders as the key itself, and it is not a data path, so the resolver's own warnings
///     never saw it: a module whose translation files never reached the localizer produced a letter made of
///     keys and no complaint.
/// </remarks>
internal sealed class ReportingTranslationResolver(IStringLocalizer text, TemplateDataContext data)
    : ITranslationResolver
{
    private readonly StringLocalizerTranslationResolver _inner = new(text);

    public string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, CultureInfo culture)
    {
        var localized = Equals(culture, CultureInfo.InvariantCulture) ? text : text.WithCulture(culture.Name);
        if (localized[key].IsMissing)
            data.AddWarning(new TemplateWarning($"t:{key}", $"No translation for '{key}' in {localized.Culture}."));

        return _inner.Resolve(key, parameters, culture);
    }
}
