using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.E2E.Tests;

/// <summary>In-memory IStringLocalizer for E2E tests.</summary>
internal sealed class TestLocalizer(string culture, Dictionary<string, string> translations) : IStringLocalizer
{
    public string Culture { get; } = culture;

    public TranslationResult this[string key] =>
        translations.TryGetValue(key, out var value)
            ? TranslationResult.Found(key, value)
            : TranslationResult.Missing(key);

    public TranslationResult this[string key, params object[] args]
    {
        get
        {
            if (!translations.TryGetValue(key, out var template))
                return TranslationResult.Missing(key);
            return TranslationResult.Found(key, string.Format(template, args));
        }
    }

    public TranslationResult Plural(string key, int count) => this[key];
    public TranslationResult Plural(string key, int count, params object[] args) => this[key, args];
    public IStringLocalizer WithCulture(string culture) => this;
}
