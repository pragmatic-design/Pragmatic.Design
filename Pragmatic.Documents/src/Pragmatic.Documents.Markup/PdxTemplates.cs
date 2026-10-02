using Pragmatic.Documents.Email;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Email.Templates;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Finds a template in its source, parses it, resolves it and its partials against the data inside
///     the reader's language, and — for a mail — renders the HTML and the text.
/// </summary>
/// <param name="source">Where templates and their partials are found.</param>
/// <param name="text">Resolves <c>t:</c> expressions, when the data context brings no resolver of its own.</param>
/// <param name="pipes">The pipes a template may use; the built-in set plus <c>date</c>, <c>currency</c>, <c>percent</c> when omitted.</param>
public sealed class PdxTemplates(IPdxTemplateSource source, IStringLocalizer text, PipeRegistry? pipes = null)
    : IPdxTemplates
{
    private static readonly EmailHtmlRenderer Html = new();

    private readonly PipeRegistry _pipes = pipes ?? PipeRegistry.Default.WithI18N();

    /// <inheritdoc />
    public async ValueTask<ComposedDocument> DocumentAsync(
        string name, string culture, TemplateDataContext data, CancellationToken ct = default)
    {
        var markup = await MarkupOfAsync(name, culture, data, ct).ConfigureAwait(false);
        var template = PdxDocParser.Parse(markup);
        var resolver = new DocumentTemplateResolver(_pipes).WithPartialProvider(new SourceDocumentPartials(source));

        // ⚠️ Inside the reader's language, not only with it on the data context: `t:` asks the localizer,
        // which reads the ambient culture, while the pipes read the context's. Setting only one gave
        // Italian dates in English sentences.
        var model = await I18NContext
            .WithCultureAsync(culture, () => resolver.ResolveAsync(template, data, ct).AsTask())
            .ConfigureAwait(false);

        return new ComposedDocument(model, data.Warnings);
    }

    /// <inheritdoc />
    public async ValueTask<ComposedEmail> EmailAsync(
        string name, string culture, TemplateDataContext data, CancellationToken ct = default)
    {
        var markup = await MarkupOfAsync(name, culture, data, ct).ConfigureAwait(false);
        var template = PdxEmailParser.Parse(markup);
        var resolver = new EmailTemplateResolver(_pipes).WithPartialProvider(new SourceEmailPartials(source));

        var model = await I18NContext
            .WithCultureAsync(culture, () => resolver.ResolveAsync(template, data, ct).AsTask())
            .ConfigureAwait(false);

        return new ComposedEmail(
            model,
            model.Subject ?? "",
            model.Preheader,
            Html.Render(model),
            EmailTextRenderer.Render(model),
            data.Warnings);
    }

    private async ValueTask<string> MarkupOfAsync(
        string name, string culture, TemplateDataContext data, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(data);

        data.WithCulture(culture);
        if (data.TranslationResolver is null)
            data.WithTranslationResolver(new ReportingTranslationResolver(text, data));

        return await source.FindAsync(name, ct).ConfigureAwait(false)
            ?? throw new PdxTemplateNotFoundException(name, source);
    }
}
