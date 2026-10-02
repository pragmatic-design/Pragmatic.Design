using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Templating.I18N;

/// <summary>
/// Extension methods to integrate Pragmatic.Internationalization with the template engine.
/// </summary>
public static class I18NTemplatingExtensions
{
    /// <summary>All I18N pipes: date, currency, percent.</summary>
    public static IReadOnlyList<ITemplatePipe> I18NPipes =>
        [new DatePipe(), new CurrencyPipe(), new PercentPipe()];

    /// <summary>
    /// Add I18N pipes (date, currency, percent) to the pipe registry.
    /// </summary>
    public static PipeRegistry WithI18N(this PipeRegistry registry)
        => registry.With([.. I18NPipes]);

    /// <summary>
    /// Configure the data context with an IStringLocalizer for <c>t:</c> expression resolution.
    /// </summary>
    public static TemplateDataContext WithLocalizer(this TemplateDataContext context, IStringLocalizer localizer)
        => context.WithTranslationResolver(new StringLocalizerTranslationResolver(localizer));
}
