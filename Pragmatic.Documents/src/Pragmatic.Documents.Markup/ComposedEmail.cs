using Pragmatic.Documents.Templating.Data;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     A mail template resolved against its data and rendered: everything a message needs, from one model.
/// </summary>
/// <param name="Model">The resolved mail.</param>
/// <param name="Subject">The subject — an expression in the template, so the template owns it too.</param>
/// <param name="Preheader">The preview line some clients show after the subject.</param>
/// <param name="Html">The HTML body. Values from the data are encoded.</param>
/// <param name="Text">The plain-text body, from the same model — never a second copy of the words.</param>
/// <param name="Warnings">Paths the template named and the data did not have.</param>
public sealed record ComposedEmail(
    EmailModel Model,
    string Subject,
    string? Preheader,
    string Html,
    string Text,
    IReadOnlyList<TemplateWarning> Warnings);
