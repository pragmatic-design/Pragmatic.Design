using Casework.Intake.Letters;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Spreadsheet;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;
using Pragmatic.Storage;

namespace Casework.Intake.Infrastructure.Letters;

/// <summary>
///     The decision letter, from the organisation's own template when it has one.
/// </summary>
/// <remarks>
///     <para>
///         <b>Two decisions are this class's, and nothing else.</b> Which language the letter is in, and
///         which names the template may write against. Finding the markup, parsing it, resolving it with
///         its letterhead and doing all of it inside the applicant's language is
///         <see cref="PdxTemplates" />'s.
///     </para>
///     <para>
///         ⚠️ <b>The fallback is declared, not silent.</b> <see cref="WrittenLetter.FromTheOrganisationsOwnTemplate" />
///         says which one was used, because "the letter rendered" is true of both and an organisation
///         that uploaded a template and is still getting the application's one has a problem nobody would
///         otherwise see.
///     </para>
///     <para>
///         ⚠️ <b>The letterhead is looked up piece by piece, from the same two places.</b> An organisation
///         that uploads only a letterhead keeps the application's wording and gets its own heading; one
///         that uploads only a letter imports the application's letterhead. The two files are replaced
///         independently because that is how an organisation actually changes them.
///     </para>
/// </remarks>
[Service<IWriteTheDecisionLetter>]
public sealed partial class TheDecisionLetter(
    IReadRepository<Case> cases,
    IReadRepository<LetterTemplate> templates,
    IFileStorage files,
    ITenantContext organisation,
    ITenantStore register,
    IStringLocalizer text,
    IConfiguredCultures cultures) : IWriteTheDecisionLetter
{
    /// <summary>The application's own pieces, beside the application.</summary>
    /// <remarks>
    ///     Files and not embedded resources, so that "change the wording" stays true of the fallback as
    ///     well — an operator edits them where they are.
    /// </remarks>
    private static readonly DirectoryPdxTemplateSource TheApplicationsOwn =
        new(Path.Combine(AppContext.BaseDirectory, "templates", "letters"));

    private static readonly string LetterFile = LetterTemplate.Letter + ".pdxdoc";

    public async Task<WrittenLetter> ForCaseAsync(Guid caseId, CancellationToken ct = default)
    {
        var @case = await cases.GetByIdAsync(caseId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"There is no case {caseId} in this organisation to write a letter about.");

        var theirs = new TheOrganisationsOwnTemplates(templates, files);
        var letters = new PdxTemplates(new FirstFoundPdxTemplateSource(theirs, TheApplicationsOwn), text);

        // The culture is the case's own, frozen when it was opened. An operator whose console is in
        // English renders exactly the same letter as the overnight job, which is what makes "in the
        // applicant's language" assertable at all.
        var letter = await letters
            .DocumentAsync(LetterFile, LanguageOf(@case), await DataForAsync(@case, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

        return new WrittenLetter(letter.Model, letter.Warnings, theirs.Supplied(LetterFile));
    }

    /// <summary>The language this letter is written in: the applicant's, or the application's.</summary>
    /// <remarks>
    ///     <para>
    ///         An applicant who did not say is not an applicant who reads nothing — they get the language
    ///         the host was <b>configured</b> with.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Configured, and deliberately not <c>I18NContext.Current</c>.</b> With no scope open
    ///         that property falls back to <c>CultureInfo.CurrentCulture</c> — the thread's — and after an
    ///         await that is whatever the last piece of work on this thread happened to leave there.
    ///         Measured here: a letter for an applicant who named no language came out in Italian, because
    ///         another test had rendered an Italian one on the same thread — and that is a job's situation
    ///         as much as a test's.
    ///     </para>
    /// </remarks>
    private string LanguageOf(Case @case)
        => @case.ApplicantLanguage is { Length: > 0 } theirs ? theirs : cultures.Default.Code;

    /// <summary>
    ///     The named roots a template writes against: <c>case</c>, <c>applicant</c>, <c>organization</c>,
    ///     <c>fees</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Dictionaries and not the entities themselves, and that is a decision: what a template may
    ///         name is then a list somebody wrote down, rather than every property the entity happens to
    ///         have. An organisation's template cannot walk from the case to a navigation nobody meant to
    ///         publish.
    ///     </para>
    ///     <para>
    ///         ⚠️ The names are the template's vocabulary, so they are the application's public surface
    ///         for letters: renaming <c>case.number</c> breaks every organisation's template silently —
    ///         a warning, not an error. That is the cost of "without a deployment" and it is worth
    ///         knowing before the first organisation uploads anything.
    ///     </para>
    /// </remarks>
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
                ["decidedOn"] = @case.VerificationAnsweredOn
            })
            .AddSource("applicant", new Dictionary<string, object?>
            {
                ["name"] = @case.Applicant,
                ["email"] = @case.ApplicantEmail
            })
            .AddSource("organization", new Dictionary<string, object?>
            {
                ["name"] = known?.TenantName ?? organisation.TenantId,
                ["address"] = null
            })
            .AddSource("fees", TheOrganisationsFeeTable(ct));
    }

    /// <summary>
    ///     The organisation's own fee table, read from where it uploaded it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Lazy, and that is the whole reason it can be here at all.</b>
    ///         <c>AddSource(name, factory)</c> resolves only when the template names <c>fees</c>, so a
    ///         letter that never mentions the table does not fetch it. Adding it eagerly would turn
    ///         every decision letter into a storage read for data most of them ignore.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>CsvStreamDataSource</c> and not <c>CsvFileDataSource</c>: what the organisation
    ///         uploaded has no path. It is behind <c>IFileStorage</c> — local disk in this host, object
    ///         storage in a deployment — and the file-backed source could only read it by going around
    ///         that. This is the case the package takes a stream for.
    ///     </para>
    ///     <para>
    ///         An organisation with no table gets a source that resolves to <c>null</c>, which is a
    ///         warning from the resolver if a template names it and nothing at all if none does — the
    ///         same shape as every other name a template may get wrong.
    ///     </para>
    /// </remarks>
    private Func<CancellationToken, ValueTask<object?>> TheOrganisationsFeeTable(CancellationToken outer)
        => async _ =>
        {
            var row = await templates
                .FirstOrDefaultAsync(LetterTemplateSpecifications.ThePiece(LetterTemplate.Fees), outer)
                .ConfigureAwait(false);

            if (row is null)
                return null;

            var source = new CsvStreamDataSource(
                "fees",
                async ct => await files
                        .GetAsync(new Uri(row.StoredAt, UriKind.RelativeOrAbsolute), ct)
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The fee table row points at {row.StoredAt} and the store has no such file."));

            return await source.ResolveAsync(outer).ConfigureAwait(false);
        };
}
