using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Casework.Intake;
using Casework.Intake.Entities;
using Casework.Intake.Infrastructure.Mail;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Email.Testing;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The mail that carries the decision is the organisation's: its template, its sender,
///     the applicant's language, and the letter that was stored attached to it.
/// </summary>
/// <remarks>
///     <para>
///         <b>One decision, one mail, and everything about it belongs to somebody.</b> The wording is the
///         organisation's (a <c>.pdxemail</c> it uploaded, with its own header as a partial), the
///         address it comes from is the organisation's, the language is the applicant's, and the
///         attachment is the letter that was rendered and <b>stored</b> — not a second rendering.
///     </para>
///     <para>
///         ⚠️ <b>The attachment is asserted by hash.</b> That is what makes "the letter that was stored"
///         a measurement rather than a hope: two renderings of the same letter are two files, and a test
///         that compared lengths or looked for a name in PDF bytes would pass for either.
///     </para>
/// </remarks>
public sealed class TheMailComesFromTheOrganisation(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    private InMemoryTransport _mailbox = null!;

    /// <summary>
    ///     The mail never leaves the process: the transport is replaced, which is what makes the message
    ///     assertable at all.
    /// </summary>
    protected override void ConfigureIntake(IServiceCollection services)
        => _mailbox = services.AddEmailTestHarness();

    [Fact]
    public async Task ADecisionMailsTheApplicantFromTheOrganisationsOwnAddress()
    {
        const string rousay = "rousay";
        await OnboardAsync(IntakeServices, rousay, null);

        // ⚠️ The queue has to be bound before the decision is published: the exchange is a topic one
        // and a message with nothing bound to it is discarded, which would fail this test for a reason
        // no deployment has.
        await WaitForSubscriberAsync("case-decided");

        await SetTheSenderAsync(rousay, "licensing@rousay.example", "Rousay Licensing");
        await UploadAsync(rousay, TheirMail, LetterTemplate.Mail);

        var decided = await ADecidedCaseFor(rousay, "en-US", "e.applicant@example.org");

        // ⚠️ Waited for, because the decision and the mail are not in one breath: the event is captured
        // into the outbox with the decision and delivered by the pump afterwards, which is what keeps an
        // applicant from being written to about a decision a failed commit rolled back.
        // The row exists, so the question is whether the pump delivers it: waited for explicitly,
        // because "the mail never arrived" and "the outbox never moved" are two different failures.
        // ⚠️ Narrowed to THIS decision's row. The outbox table is on the shared schema and keeps every
        // row this class's other tests wrote, so a count over the whole table counts the suite: it is 1
        // when this test runs alone and 3 when it runs third, which is a test that passes or fails on
        // its position in the class.
        await EventuallyAsync(
            async () => (long?)await ScalarAsync(
                IntakeConnectionString,
                $"""
                select count(*) from "__OutboxMessages"
                where "MessageType" like '%CaseDecided' and "ProcessedAt" is not null
                  and "Payload" like '%{decided.Id}%'
                """) == 1,
            "the outbox delivered the decision", timeoutSeconds: 30);

        await EventuallyAsync(
            () => Task.FromResult(MailAbout(decided.Number).Count > 0), "the decision was mailed");

        var sent = MailAbout(decided.Number).Should().ContainSingle(
            "one decision, one mail").Subject.Message;

        // Whose mail it is: the organisation's address and its own wording, from its own template.
        sent.From.Address.Should().Be("licensing@rousay.example");
        sent.From.DisplayName.Should().Be("Rousay Licensing");
        sent.To.Should().ContainSingle().Which.Address.Should().Be("e.applicant@example.org");

        sent.Subject.Should().Contain(decided.Number);
        (sent.HtmlBody ?? "").Should().Contain("Rousay Licensing writes about your application");
        (sent.TextBody ?? "").Should().Contain(decided.Number);

        // And the attachment is the letter that was stored: the same bytes, by hash.
        var attachment = sent.Attachments.Should().ContainSingle().Subject;
        attachment.FileName.Should().Be($"{decided.Number}.pdf");
        attachment.ContentType.Should().Be("application/pdf");

        Hash(attachment.Data.ToArray()).Should().Be(
            Hash(await TheStoredLetterAsync(rousay, decided.Number)),
            "the attachment is the file that was stored, not a second rendering of the same letter");
    }

    /// <summary>
    ///     The applicant's language, in the mail as in the letter.
    /// </summary>
    [Fact]
    public async Task TheMailIsInTheApplicantsLanguage()
    {
        const string egilsay = "egilsay";
        await OnboardAsync(IntakeServices, egilsay, null);

        await SetTheSenderAsync(egilsay, "pratiche@egilsay.example", "Comune di Egilsay");

        var decided = await ADecidedCaseFor(egilsay, "it-IT", "r.richiedente@example.org");

        await EventuallyAsync(
            () => Task.FromResult(MailAbout(decided.Number).Count > 0), "the decision was mailed");

        var sent = MailAbout(decided.Number).Should().ContainSingle().Subject.Message;

        sent.Subject.Should().Contain("Decisione sulla pratica");
        (sent.TextBody ?? "").Should().Contain("Gentile");
        (sent.TextBody ?? "").Should().NotContain("Dear");
    }

    /// <summary>
    ///     The control the story asks for: an organisation with no template of its own gets the
    ///     application's, and it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task AnOrganisationWithNoTemplateSendsTheApplicationsOwnMail()
    {
        const string shapinsay = "shapinsay";

        // ⚠️ Registered under a name that is not its id, so the last assertion measures the
        // organisation's NAME. And under an id no other test uses: OnboardAsync leaves an organisation
        // that already exists alone, so a shared id would take whatever name the class that got there
        // first gave it — which is the id, and an assertion on the name that reads the id proves
        // nothing.
        await OnboardAsync(IntakeServices, shapinsay, null, tenantName: "Shapinsay Council");

        await SetTheSenderAsync(shapinsay, "licensing@shapinsay.example", "Shapinsay Council");

        var decided = await ADecidedCaseFor(shapinsay, "en-US", "s.applicant@example.org");

        await EventuallyAsync(
            () => Task.FromResult(MailAbout(decided.Number).Count > 0), "the decision was mailed");

        var sent = MailAbout(decided.Number).Should().ContainSingle().Subject.Message;

        sent.From.Address.Should().Be("licensing@shapinsay.example",
            "the sender is still the organisation's: only the wording falls back");
        sent.Subject.Should().Be($"Decision on case {decided.Number}",
            "which is the application's own subject line, from its own .pdxemail");
        (sent.HtmlBody ?? "").Should().Contain("Shapinsay Council",
            "and the application's template still writes the organisation's name at the top");
    }

    /// <summary>
    ///     The second control: no sender address, no mail — and not a mail from the application's own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This one and the next assert that nothing is sent, and they stayed live while the three
    ///     above were once skipped — so the skip could not hide a regression in the refusal.
    ///     Now that the delivery works they say the same thing about a mail that would otherwise be
    ///     sent, which is the stronger version of the claim.
    /// </remarks>
    /// <remarks>
    ///     ⚠️ This is the one that keeps the story's claim honest. A default sender would make every
    ///     other test here pass while sending every organisation's applicants mail from one address, and
    ///     an applicant who replied would reach the wrong people about somebody else's case.
    /// </remarks>
    [Fact]
    public async Task AnOrganisationWithNoSenderAddressSendsNothing()
    {
        const string gairsay = "gairsay";
        await OnboardAsync(IntakeServices, gairsay, null);

        // No `PUT api/correspondence` for this one.
        var decided = await ADecidedCaseFor(gairsay, "en-US", "g.applicant@example.org");

        // Long enough for the pump to have delivered the event and the handler to have refused: an
        // assertion on "nothing happened" has to give the thing a chance to happen.
        await Task.Delay(TimeSpan.FromSeconds(5));

        MailAbout(decided.Number).Should().BeEmpty(
            "an organisation that has not said where its mail comes from does not send any");
    }

    /// <summary>An applicant with no address is not written to either.</summary>
    [Fact]
    public async Task AnApplicantWithNoAddressIsNotWrittenTo()
    {
        const string eynhallow = "eynhallow";
        await OnboardAsync(IntakeServices, eynhallow, null);

        await SetTheSenderAsync(eynhallow, "licensing@eynhallow.example", "Eynhallow Council");
        var decided = await ADecidedCaseFor(eynhallow, "en-US", applicantEmail: null);

        await Task.Delay(TimeSpan.FromSeconds(5));

        MailAbout(decided.Number).Should().BeEmpty();
    }

    /// <summary>
    ///     The letterhead is a piece of its own here too: an organisation can replace only that, and
    ///     keep the application's wording around it.
    /// </summary>
    /// <remarks>
    ///     The mail's half of the trade the letter offers (<c>AnOrganisationCanReplaceOnlyTheLetterhead</c>),
    ///     and the only test in which the body carries <b>this organisation's own partial</b>: the three
    ///     above either upload the whole mail or nothing, so the partial they render is the
    ///     application's. Without this one, <c>IEmailPartialProvider</c> resolving a per-tenant piece is
    ///     a code path nothing exercises.
    /// </remarks>
    [Fact]
    public async Task AnOrganisationCanReplaceOnlyTheMailsLetterhead()
    {
        const string faray = "faray";
        await OnboardAsync(IntakeServices, faray, null);

        await SetTheSenderAsync(faray, "licensing@faray.example", "Faray Council");

        // Only the letterhead. No decision-mail row, so the wording below it stays the application's.
        await UploadAsync(
            faray,
            """
            <email>
              <article>
                <heading>Faray Parish — Licensing</heading>
                <text>Post Office Buildings, Faray</text>
              </article>
            </email>
            """,
            LetterTemplate.MailHeader);

        var decided = await ADecidedCaseFor(faray, "en-US", "f.applicant@example.org");

        await EventuallyAsync(
            () => Task.FromResult(MailAbout(decided.Number).Count > 0), "the decision was mailed");

        var sent = MailAbout(decided.Number).Should().ContainSingle().Subject.Message;

        (sent.HtmlBody ?? "").Should().Contain("Faray Parish — Licensing",
            "the partial in the body is this organisation's");
        (sent.HtmlBody ?? "").Should().Contain("Post Office Buildings, Faray");
        (sent.HtmlBody ?? "").Should().Contain("The decision letter is attached to this message",
            "and the wording around it is still the application's, which is what a partial is for");
        sent.Subject.Should().Be($"Decision on case {decided.Number}",
            "the subject comes from the application's mail, which this organisation did not replace");
    }

    /// <summary>
    ///     The control the story asks for on the mail's side: a template that names something nobody
    ///     provides is reported <b>here</b>, through the warnings — not in an applicant's letterbox.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The twin of <c>TheLetterIsTheOrganisationsOwn.ATemplateThatAsksForSomethingNobodyProvidesIsReported</c>,
    ///     and needed for the same reason: a missing property is not an error. The expression resolves to
    ///     null, the resolver records a <c>TemplateWarning</c>, the mail goes out with a hole in it and
    ///     the send succeeds. "The template resolved" and "the data was there" are two statements and
    ///     only the second is worth anything to an applicant.
    ///     Asserted on <c>ComposedMail</c> rather than on the captured message, because the warnings are
    ///     what is being asserted and the handler only writes them to a log.
    /// </remarks>
    [Fact]
    public async Task AMailTemplateThatAsksForSomethingNobodyProvidesIsReported()
    {
        const string swona = "swona";
        const string copinsay = "copinsay";

        await OnboardAsync(IntakeServices, swona, null);
        await OnboardAsync(IntakeServices, copinsay, null);

        await SetTheSenderAsync(swona, "licensing@swona.example", "Swona Council");
        await SetTheSenderAsync(copinsay, "licensing@copinsay.example", "Copinsay Council");

        await UploadAsync(
            swona,
            """
            <email subject="Decision on {{ case.number }}">
              <article>
                <text>Your reference is {{ case.reference }}</text>
              </article>
            </email>
            """,
            LetterTemplate.Mail);

        var asked = await ADecidedCaseFor(swona, "en-US", "s.applicant@example.org");
        var didnt = await ADecidedCaseFor(copinsay, "en-US", "c.applicant@example.org");

        var reported = await ComposedFor(swona, asked.Id);
        var quiet = await ComposedFor(copinsay, didnt.Id);

        reported!.FromTheOrganisationsOwnTemplate.Should().BeTrue();
        reported.Warnings.Should().NotBeEmpty(
            "a property that does not exist resolves to null and is recorded, not thrown");
        reported.Warnings.Select(warning => warning.Path).Should()
            .Contain(path => path.Contains("reference", StringComparison.Ordinal));

        // And the mail really does have the hole the warning is about, which is what nobody should be
        // discovering the other way round.
        (reported.Message.HtmlBody ?? "").Should().Contain("Your reference is ");

        // The control: the application's own mail asks for nothing it does not provide, so a warning
        // here would mean the channel reports on every mail and says nothing about any of them.
        quiet!.FromTheOrganisationsOwnTemplate.Should().BeFalse();
        quiet.Warnings.Should().BeEmpty();
    }

    /// <summary>
    ///     The mail as the application composes it, inside that organisation's tenant.
    /// </summary>
    /// <remarks>
    ///     Through the service and not the mailbox: what is asserted is the <c>ComposedMail</c>, and its
    ///     warnings never reach the message. The attachment is a stand-in — these bytes are not what is
    ///     being asserted, and <c>ADecisionMailsTheApplicantFromTheOrganisationsOwnAddress</c> is where
    ///     the real one is checked by hash.
    /// </remarks>
    private async Task<ComposedMail?> ComposedFor(string tenantId, Guid caseId)
        => await AsTenantAsync(
            async () =>
            {
                using var scope = IntakeServices.CreateScope();

                return await scope.ServiceProvider
                    .GetRequiredService<ITellTheApplicant>()
                    .AboutTheDecisionAsync(caseId, [1, 2, 3]);
            },
            tenantId);

    /// <summary>
    ///     The mails this test's own decision produced, and no other test's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Every host in this class binds the <b>same</b> queue — <c>intake.case-decided</c> — and
    ///     the broker outlives a test. That is correct: the name
    ///     carries the subscribing module, and every host here <em>is</em> Intake, so they are one
    ///     consumer group by design rather than by accident. A delivery whose acknowledgement did not
    ///     land before its host shut down is redelivered to whichever host binds next, so one test's
    ///     mailbox can hold a mail about another's case:
    ///     measured as an assertion on the sender's address failing with
    ///     <c>pratiche@egilsay.example</c> in the test that onboarded <c>rousay</c>.
    ///     Filtering by the case number is what makes "one decision, one mail" an assertion about a
    ///     decision rather than about the order the class happened to run in.
    /// </remarks>
    private IReadOnlyList<SentEmail> MailAbout(string number)
        => _mailbox.SentWhere(m => (m.Subject ?? "").Contains(number, StringComparison.Ordinal));

    /// <summary>This organisation's own mail: its wording, and its own header as a partial.</summary>
    private const string TheirMail =
        """
        <email subject="Decision on {{ case.number }}" preheader="Your application has been decided.">
          <article>
            <partial name="mail-header" />
            <text>Rousay Licensing writes about your application {{ case.number }}.</text>
            <text>{{ t:mail.decision.attachment }}</text>
          </article>
        </email>
        """;

    private async Task SetTheSenderAsync(string tenantId, string address, string name)
        => (await IntakeAs(Caseworker, tenantId).PutAsJsonAsync(
                "api/correspondence", new { senderAddress = address, senderName = name }))
            .IsSuccessStatusCode.Should().BeTrue("{0} says where its mail comes from", tenantId);

    private async Task UploadAsync(string tenantId, string markup, string piece)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(markup));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        form.Add(file, "file", $"{piece}.pdxemail");
        form.Add(new StringContent(piece), "piece");

        var sent = await IntakeAs(Caseworker, tenantId).PostAsync("api/letter-templates", form);

        sent.IsSuccessStatusCode.Should().BeTrue("{0}", await sent.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     A case taken all the way to a decision, which is what raises <c>CaseDecided</c> and therefore
    ///     what sends the mail.
    /// </summary>
    /// <remarks>
    ///     Through the boundary's internal actions rather than over the bus: in production the saga
    ///     decides a case (<c>TheProcessThatCarriesACase</c> proves it), and what this story is about is
    ///     what happens <b>after</b> a decision, whoever asked for it.
    /// </remarks>
    private async Task<(Guid Id, string Number)> ADecidedCaseFor(
        string tenantId, string language, string? applicantEmail)
    {
        var opened = await ReadJsonAsync(await IntakeAs(Caseworker, tenantId).PostAsJsonAsync(
            "api/cases",
            new
            {
                subject = $"A licence for a shop in {tenantId}",
                applicant = "A. Applicant",
                applicantEmail,
                applicantLanguage = language
            }));

        var id = opened.GetProperty("id").GetGuid();
        var number = opened.GetProperty("number").GetString()!;

        (await IntakeAs(Caseworker, tenantId).PostAsJsonAsync(
                $"api/cases/{id}/verifications", new { kind = "identity" }))
            .IsSuccessStatusCode.Should().BeTrue();

        await AsTenantAsync(
            async () =>
            {
                using var scope = IntakeServices.CreateScope();
                var intake = scope.ServiceProvider.GetRequiredService<IIntakeInternalActions>();

                await intake.Cases.RecordVerificationOutcome(
                    caseId: id,
                    eventId: Guid.CreateVersion7(),
                    outcome: Casework.Verify.Events.VerificationOutcome.Passed);

                await intake.Cases.DecideCase(
                    caseId: id, outcome: Casework.Verify.Events.VerificationOutcome.Passed);

                return true;
            },
            tenantId);

        return (id, number);
    }

    /// <summary>The letter as it was filed in this organisation's container.</summary>
    private async Task<byte[]> TheStoredLetterAsync(string tenantId, string number)
    {
        var container = Path.Combine(IntakeStorageRoot, "files", "letters", tenantId);
        var files = Directory.GetFiles(container, "*.pdf");

        files.Should().NotBeEmpty("the decision letter was stored before it was attached");

        return await File.ReadAllBytesAsync(files[0]);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
