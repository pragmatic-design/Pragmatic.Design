using Pragmatic.Email.Templates;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     Resolves a mail's <c>&lt;partial name="…" /&gt;</c> from the same source as the mail.
/// </summary>
internal sealed class SourceEmailPartials(IPdxTemplateSource source) : IEmailPartialProvider
{
    public async ValueTask<EmailPartialDefinition?> GetAsync(string name, CancellationToken ct = default)
    {
        var file = Path.HasExtension(name) ? name : name + ".pdxemail";
        var markup = await source.FindAsync(file, ct).ConfigureAwait(false);

        if (markup is null)
            return null;

        var parsed = PdxEmailParser.Parse(markup);

        return new EmailPartialDefinition { Name = name, Sections = parsed.Sections };
    }
}
