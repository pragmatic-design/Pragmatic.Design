using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;

namespace Casework.Intake.Infrastructure.Letters;

/// <summary>
///     Builds the decision letter of a case from whichever template that organisation uses.
/// </summary>
public interface IWriteTheDecisionLetter
{
    /// <summary>
    ///     The letter as a document model — what it says, not how it is drawn.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A <see cref="DocumentModel" /> and not bytes, deliberately. Rendering is chosen where the
    ///     bytes are wanted (one endpoint renders a PDF, another a Word file), and a model
    ///     is the only form in which what the letter <b>says</b> can be asserted at all — a PDF's text is
    ///     compressed, so searching its bytes finds a name neither when it is there nor when it is not.
    /// </remarks>
    Task<WrittenLetter> ForCaseAsync(Guid caseId, CancellationToken ct = default);
}

/// <summary>
///     A letter, and what the template asked for and did not get.
/// </summary>
/// <param name="Model">The resolved document.</param>
/// <param name="Warnings">
///     ⚠️ <b>Read them.</b> A missing property is not an error: the expression resolves to null and the
///     data context records the path it could not follow. So "the template resolved" does not mean "the
///     data was there", and an application that ignores this channel sends letters with holes in them.
/// </param>
/// <param name="FromTheOrganisationsOwnTemplate">
///     Whether this came from the organisation's uploaded template or from the application's fallback.
/// </param>
public sealed record WrittenLetter(
    DocumentModel Model,
    IReadOnlyList<TemplateWarning> Warnings,
    bool FromTheOrganisationsOwnTemplate);
