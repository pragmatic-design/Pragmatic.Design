using System.Globalization;

namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>
/// Resolves translation expressions (<c>t:key</c>).
/// Implemented by the I18N integration package using <c>IStringLocalizer</c>.
/// </summary>
public interface ITranslationResolver
{
    /// <summary>Resolve a translation key with optional named parameters, in the given culture.</summary>
    /// <param name="key">The translation key.</param>
    /// <param name="parameters">The named parameters, or <see langword="null" />.</param>
    /// <param name="culture">
    ///     The document's culture — <c>TemplateDataContext.Culture</c>, the one the pipes format with.
    ///     <see cref="CultureInfo.InvariantCulture" /> when the context set none: resolve in the ambient
    ///     culture then. It is a parameter because resolving in the ambient culture regardless is how a
    ///     document asked for in Italian printed Italian dates inside English sentences.
    /// </param>
    string Resolve(string key, IReadOnlyDictionary<string, object?>? parameters, CultureInfo culture);
}
