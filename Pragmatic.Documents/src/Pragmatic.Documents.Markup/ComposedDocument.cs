using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Markup;

/// <summary>
///     A document template resolved against its data: the model to render — PDF, DOCX — and what the
///     template asked for and did not get.
/// </summary>
/// <param name="Model">The resolved document; render it with <c>PdfRenderer</c> or another renderer.</param>
/// <param name="Warnings">
///     Paths the template named and the data did not have. A template edited by somebody else renders
///     anyway; these are how a caller finds out it no longer fits the data.
/// </param>
public sealed record ComposedDocument(DocumentModel Model, IReadOnlyList<TemplateWarning> Warnings);
