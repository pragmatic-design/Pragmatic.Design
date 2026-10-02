using Pragmatic.Documents.Templates;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Resolves a document's <c>&lt;import src="…" /&gt;</c> / <c>&lt;partial name="…" /&gt;</c> from the
///     same source as the document, so a letterhead is overridden where the letter is.
/// </summary>
internal sealed class SourceDocumentPartials(IPdxTemplateSource source) : IDocumentPartialProvider
{
    public async ValueTask<DocumentPartialTemplate?> GetAsync(string name, CancellationToken ct = default)
    {
        var file = Path.HasExtension(name) ? name : name + ".pdxdoc";
        var markup = await source.FindAsync(file, ct).ConfigureAwait(false);

        if (markup is null)
            return null;

        // A partial is a fragment written as a whole document, so that it parses and previews on its
        // own: its content is the content of its first page.
        var parsed = PdxDocParser.Parse(markup);

        return new DocumentPartialTemplate
        {
            Name = name,
            Content = parsed.Pages.Count > 0 ? parsed.Pages[0].Content : [],
        };
    }
}
