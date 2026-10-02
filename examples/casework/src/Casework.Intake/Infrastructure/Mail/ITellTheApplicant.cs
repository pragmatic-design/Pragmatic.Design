using Pragmatic.Documents.Templating.Data;
using Pragmatic.Email;

namespace Casework.Intake.Infrastructure.Mail;

/// <summary>
///     Composes the mail that tells an applicant how their case ended.
/// </summary>
public interface ITellTheApplicant
{
    /// <summary>
    ///     The message, ready to send — or nothing, when this organisation cannot send mail yet.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <see langword="null" /> rather than a message from a default address: the sender is the
    ///     organisation's, and an organisation that has not given one has no mail to send. The caller
    ///     says so in a log; the applicant is not written to by somebody else.
    /// </remarks>
    Task<ComposedMail?> AboutTheDecisionAsync(Guid caseId, byte[] letter, CancellationToken ct = default);
}

/// <summary>
///     A message and what its template asked for and did not get.
/// </summary>
/// <param name="Message">The mail itself: sender, recipient, subject, body, attachment.</param>
/// <param name="Warnings">
///     ⚠️ The same channel as the letter's, and read for the same reason: a missing property is not an
///     error, so "the template resolved" does not mean "the data was there".
/// </param>
/// <param name="FromTheOrganisationsOwnTemplate">
///     Whether the markup was the organisation's or the application's fallback.
/// </param>
public sealed record ComposedMail(
    EmailMessage Message,
    IReadOnlyList<TemplateWarning> Warnings,
    bool FromTheOrganisationsOwnTemplate);
