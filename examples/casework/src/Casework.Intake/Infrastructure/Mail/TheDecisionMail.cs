using Casework.Intake.Infrastructure.Letters;
using Casework.Intake.Letters;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Email;
using Pragmatic.Email.Builder;
using Pragmatic.Internationalization.Providers;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Storage;

namespace Casework.Intake.Infrastructure.Mail;

/// <summary>
///     The mail that tells an applicant how their case ended, from the organisation's own template.
/// </summary>
/// <remarks>
///     <para>
///         <b>The letter's twin, on purpose.</b> Same two places for the markup (the organisation's row,
///         then the application's file), same named roots, same <c>t:</c> translations, same
///         locale-aware pipes, same language, same warning channel. What differs is that a mail has a
///         sender, a recipient and an attachment — and a subject, which the template owns too.
///     </para>
///     <para>
///         ⚠️ <b>The sender is the organisation's address and there is no default.</b> One would send
///         every organisation's mail from the application's own, and an applicant who replied would be
///         writing to the wrong people about somebody else's case. No address means no mail: this
///         returns null and the caller records it.
///     </para>
///     <para>
///         ⚠️ <b>The attachment is the letter that was stored, passed in as bytes.</b> Rendering it
///         again here would produce a second document for one decision — and the applicant would hold
///         one while the organisation held another, with no way to tell which was sent.
///     </para>
/// </remarks>
[Service<ITellTheApplicant>]
public sealed partial class TheDecisionMail(
    IReadRepository<Case> cases,
    IReadRepository<LetterTemplate> templates,
    IReadRepository<CorrespondenceSettings> correspondence,
    IFileStorage files,
    ITenantContext organisation,
    ITenantStore register,
    IStringLocalizer text) : ITellTheApplicant
{
    private static readonly DirectoryPdxTemplateSource TheApplicationsOwn =
        new(Path.Combine(AppContext.BaseDirectory, "templates", "letters"));

    private static readonly string MailFile = LetterTemplate.Mail + ".pdxemail";

    public async Task<ComposedMail?> AboutTheDecisionAsync(
        Guid caseId, byte[] letter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(letter);

        var @case = await cases.GetByIdAsync(caseId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"There is no case {caseId} in this organisation to write to anybody about.");

        // No address to write to, or nobody to write from: either way there is no mail, and the caller
        // is the one that says so out loud.
        if (@case.ApplicantEmail is not { Length: > 0 } applicantAddress)
            return null;

        var sender = await correspondence
            .FirstOrDefaultAsync(CorrespondenceSettingsSpecifications.TheOnlyOne(), ct)
            .ConfigureAwait(false);

        if (sender is null)
            return null;

        var theirs = new TheOrganisationsOwnTemplates(templates, files);
        var mails = new PdxTemplates(new FirstFoundPdxTemplateSource(theirs, TheApplicationsOwn), text);

        var data = await DataForAsync(@case, ct).ConfigureAwait(false);
        var mail = await mails.EmailAsync(MailFile, LanguageOf(@case), data, ct).ConfigureAwait(false);

        var message = new EmailMessageBuilder()
            .From(sender.SenderAddress, sender.SenderName)
            .To(applicantAddress, @case.Applicant)
            .Subject(mail.Subject is { Length: > 0 } subject ? subject : @case.Number)
            .HtmlBody(mail.Html)
            .TextBody(mail.Text)
            // The stored letter, not a new one.
            .Attach($"{@case.Number}.pdf", letter, "application/pdf")
            .Build();

        return new ComposedMail(message, mail.Warnings, theirs.Supplied(MailFile));
    }

    /// <summary>The language this mail is written in: the applicant's, or the module's own.</summary>
    /// <remarks>
    ///     ⚠️ Not <c>I18NContext.Current</c> — outside a scope that reads the thread's culture, which
    ///     after an await is whatever the last piece of work left there (see <c>TheDecisionLetter</c>
    ///     for why the host's configured default is not reachable from here).
    /// </remarks>
    private static string LanguageOf(Case @case)
        => @case.ApplicantLanguage is { Length: > 0 } theirs ? theirs : "en-US";

    /// <summary>The same roots the letter writes against, so one vocabulary serves both.</summary>
    private async Task<TemplateDataContext> DataForAsync(Case @case, CancellationToken ct)
    {
        var known = organisation.TenantId is { Length: > 0 } tenantId
            ? await register.GetByIdAsync(tenantId, ct).ConfigureAwait(false)
            : null;

        return new TemplateDataContext()
            .AddSource("case", new Dictionary<string, object?>
            {
                ["number"] = @case.Number,
                ["subject"] = @case.Subject,
                ["status"] = @case.Status.ToString(),
                ["outcome"] = @case.VerificationOutcome?.ToString(),
                ["decidedOn"] = @case.VerificationAnsweredOn,
                ["language"] = LanguageOf(@case)
            })
            .AddSource("applicant", new Dictionary<string, object?>
            {
                ["name"] = @case.Applicant,
                ["email"] = @case.ApplicantEmail
            })
            .AddSource("organization", new Dictionary<string, object?>
            {
                ["name"] = known?.TenantName ?? organisation.TenantId
            });
    }
}
