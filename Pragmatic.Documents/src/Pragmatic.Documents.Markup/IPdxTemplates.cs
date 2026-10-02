using Pragmatic.Composition.Attributes;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     A <c>.pdxdoc</c> or <c>.pdxemail</c> template by name, resolved against its data in a language:
///     the model of a document, or the subject, HTML and text of a mail — one call.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <c>services.AddPdxTemplates(t =&gt; t.FromAssemblyOf&lt;TModule&gt;())</c>.
///         Construct <see cref="PdxTemplates" /> directly when the templates come from a source of your
///         own — an organisation's uploads before the application's files.
///     </para>
///     <para>
///         ⚠️ <b>The language is a parameter, and it is required.</b> It governs the <c>t:</c>
///         translations <b>and</b> the <c>date</c>/<c>currency</c>/<c>percent</c> pipes. The recipient's
///         language is rarely the caller's — an invoice is in the customer's, a reminder sent by a job has
///         no request at all — and a default taken from the ambient culture is how a document ends up in
///         whatever language the last piece of work on the thread left behind.
///     </para>
/// </remarks>
[ProvidedByHost(Lifetime.Scoped)]
public interface IPdxTemplates
{
    /// <summary>Resolves the document template <paramref name="name" /> in <paramref name="culture" />.</summary>
    /// <param name="name">The template's name in its source — <c>"invoice.pdxdoc"</c>.</param>
    /// <param name="culture">The language of the reader — <c>"it-IT"</c>.</param>
    /// <param name="data">The named roots the template writes against.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="PdxTemplateNotFoundException">No source has the template.</exception>
    /// <exception cref="MarkupParseException">The template is not valid markup.</exception>
    ValueTask<ComposedDocument> DocumentAsync(
        string name, string culture, TemplateDataContext data, CancellationToken ct = default);

    /// <summary>Resolves and renders the mail template <paramref name="name" /> in <paramref name="culture" />.</summary>
    /// <param name="name">The template's name in its source — <c>"reminder.pdxemail"</c>.</param>
    /// <param name="culture">The language of the recipient.</param>
    /// <param name="data">The named roots the template writes against.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="PdxTemplateNotFoundException">No source has the template.</exception>
    /// <exception cref="MarkupParseException">The template is not valid markup.</exception>
    ValueTask<ComposedEmail> EmailAsync(
        string name, string culture, TemplateDataContext data, CancellationToken ct = default);
}
