using System.Globalization;
using System.Net.Http.Json;
using Casework.Intake.Infrastructure.Letters;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.Context;
using Pragmatic.Testing.Assertions;
using static Casework.IntegrationTests.Infrastructure.LetterText;

namespace Casework.IntegrationTests;

/// <summary>
///     One template, two languages: the decision is readable to the person it decides about.
/// </summary>
/// <remarks>
///     <para>
///         <b>The same template, not a copy per language.</b> The words come from the application's
///         translation keys through <c>t:</c> expressions and the dates through the locale-aware
///         <c>date</c> pipe, so a template stays one file and an organisation that uploads its own is
///         not signing up to maintain two.
///     </para>
///     <para>
///         ⚠️ <b>The language is the case's, frozen when it was opened.</b> The letter may be rendered
///         days later by a job with no request, or by an operator whose own console is in English —
///         asking whoever is present would be asking the wrong person. That is why the assertion below
///         can be made from a test whose own culture is neither of the two.
///     </para>
/// </remarks>
public sealed class TheLetterIsInTheApplicantsLanguage(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    private const string Stronsay = "stronsay";

    [Fact]
    public async Task TwoApplicantsWithTwoLanguagesGetTwoLetters()
    {
        await OnboardAsync(IntakeServices, Stronsay, null);

        var italian = await ACaseFor("R. Richiedente", "it-IT");
        var english = await ACaseFor("E. Applicant", "en-US");

        var theirs = await LetterForAsync(italian);
        var hers = await LetterForAsync(english);

        // The words: the same key, two languages.
        TextOf(theirs).Should().Contain("Decisione");
        TextOf(theirs).Should().Contain("Pratica");
        TextOf(theirs).Should().NotContain("Decision\n");

        TextOf(hers).Should().Contain("Decision");
        TextOf(hers).Should().Contain("Case ");
        TextOf(hers).Should().NotContain("Pratica");

        // And the sentence that is not a single word, so a test cannot pass on a coincidence.
        TextOf(theirs).Should().Contain("può chiedere a stronsay di riesaminarla");
        TextOf(hers).Should().Contain("you may ask stronsay to look at it again");

        // Neither letter asked for anything the application does not provide.
        theirs.Warnings.Should().BeEmpty();
        hers.Warnings.Should().BeEmpty();
    }

    /// <summary>
    ///     The dates follow the words: one culture, not two.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A letter that says "Decisa il" and then writes the date the American way is two decisions
    ///     disagreeing inside one paragraph. The <c>date</c> pipe formats with the data context's
    ///     culture, and the scope the service opens is what makes that culture the applicant's — which
    ///     is why this is a separate assertion from the words: they can come apart.
    /// </remarks>
    [Fact]
    public async Task TheDatesFollowTheLanguageToo()
    {
        const string sanday = "sanday";
        await OnboardAsync(IntakeServices, sanday, null);

        var when = new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero);

        var italian = await ADecidedCaseFor(sanday, "it-IT", when);
        var english = await ADecidedCaseFor(sanday, "en-US", when);

        var italianLetter = TextOf(await LetterForAsync(italian, sanday));
        var englishLetter = TextOf(await LetterForAsync(english, sanday));

        // "D" is the long date of whichever culture: the month's name is the give-away, and it is a
        // different word in each — asserted against what the framework itself would produce, so this
        // does not encode one machine's idea of March.
        italianLetter.Should().Contain(when.ToString("D", CultureInfo.GetCultureInfo("it-IT")));
        englishLetter.Should().Contain(when.ToString("D", CultureInfo.GetCultureInfo("en-US")));

        italianLetter.Should().NotContain(when.ToString("D", CultureInfo.GetCultureInfo("en-US")));
    }

    /// <summary>
    ///     The control: the language is the applicant's, not the caller's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>This is the test the story asks to see fail.</b> Everything here runs inside an
    ///         Italian ambient culture — the operator's console, or a job that happened to be started
    ///         that way — and the letter still comes out in English, because the service opens the
    ///         case's own culture around the rendering. Take that scope out and this test reports the
    ///         operator's language, which is exactly the bug it exists to catch.
    ///     </para>
    ///     <para>
    ///         Without it, "the letter is in the applicant's language" is satisfied by a letter in
    ///         whatever language the test process happens to be in, which on a developer's machine is
    ///         usually the right one by accident.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheOperatorsOwnLanguageDoesNotReachTheLetter()
    {
        const string eday = "eday";
        await OnboardAsync(IntakeServices, eday, null);

        var english = await ACaseFor("E. Applicant", "en-US", eday);

        var letter = await I18NContext.WithCultureAsync(
            "it-IT",
            async () => await LetterForAsync(english, eday));

        TextOf(letter).Should().Contain("Decision",
            "the applicant reads English, whatever language the person rendering it is working in");
        TextOf(letter).Should().NotContain("Decisione");
    }

    /// <summary>An applicant who did not say gets the application's own default.</summary>
    [Fact]
    public async Task AnApplicantWhoDidNotSayGetsTheDefault()
    {
        const string papa = "papawestray";
        await OnboardAsync(IntakeServices, papa, null);

        var silent = await ACaseFor("S. Applicant", language: null, tenant: papa);

        TextOf(await LetterForAsync(silent, papa)).Should().Contain("Decision",
            "the host's default culture is en-US, and an applicant who did not say is not one who reads nothing");
    }

    private async Task<Guid> ACaseFor(string applicant, string? language, string tenant = Stronsay)
    {
        var opened = await ReadJsonAsync(await IntakeAs(Caseworker, tenant).PostAsJsonAsync(
            "api/cases",
            new
            {
                subject = "A licence for a shop by the pier",
                applicant,
                applicantEmail = "someone@example.org",
                applicantLanguage = language
            }));

        return opened.GetProperty("id").GetGuid();
    }

    /// <summary>A case whose verification came back, so the letter has a decision and a date in it.</summary>
    private async Task<Guid> ADecidedCaseFor(string tenant, string language, DateTimeOffset when)
    {
        var caseId = await ACaseFor("A. Applicant", language, tenant);

        // Straight onto the entity, inside its own tenant: the round trip through Verify is stories 7
        // and 8, and what this story needs is a case that carries an outcome and the day it arrived.
        await AsTenantAsync(
            async () =>
            {
                using var scope = IntakeServices.CreateScope();
                var cases = scope.ServiceProvider
                    .GetRequiredService<Pragmatic.Persistence.Repository.IRepository<Casework.Intake.Entities.Case>>();
                var unitOfWork = scope.ServiceProvider
                    .GetRequiredKeyedService<Pragmatic.Persistence.Repository.IUnitOfWork>(
                        typeof(Casework.Intake.IntakeBoundary));

                var @case = await cases.GetByIdAsync(caseId);
                @case!.AskForVerification("identity", when.AddDays(-1));
                @case.RecordVerificationOutcome(
                    Guid.CreateVersion7(), Casework.Verify.Events.VerificationOutcome.Passed, when);

                await unitOfWork.SaveChangesAsync();

                return true;
            },
            tenant);

        return caseId;
    }

    private async Task<WrittenLetter> LetterForAsync(Guid caseId, string tenant = Stronsay)
        => await AsTenantAsync(
            async () =>
            {
                using var scope = IntakeServices.CreateScope();

                return await scope.ServiceProvider
                    .GetRequiredService<IWriteTheDecisionLetter>()
                    .ForCaseAsync(caseId);
            },
            tenant);
}
