using Pragmatic.Composition;
using Pragmatic.Internationalization.AspNetCore.Extensions;

namespace Pragmatic.Internationalization;

/// <summary>
///     Extension methods for configuring I18N on <see cref="IPragmaticBuilder"/>.
/// </summary>
public static class PragmaticBuilderI18NExtensions
{
    /// <summary>
    ///     Configures internationalization for the application.
    /// </summary>
    /// <param name="builder">The Pragmatic builder.</param>
    /// <param name="configure">Action to configure I18N via the builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.UseI18N(i18n =>
    /// {
    ///     i18n.DefaultCulture(CultureCode.Italian);
    ///     i18n.Support(CultureCode.Italian, CultureCode.EnglishUS);
    ///     i18n.AddScope&lt;InvoicingScope&gt;();
    /// });
    /// </code>
    /// </example>
    public static IPragmaticBuilder UseI18N(
        this IPragmaticBuilder builder,
        Action<I18NBuilder> configure)
    {
        var i18nBuilder = builder.Services.AddPragmaticInternationalization();
        configure(i18nBuilder);
        return builder;
    }
}
