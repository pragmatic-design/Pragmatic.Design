using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Casework.Intake.Infrastructure.Letters;
using Casework.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using static Casework.IntegrationTests.Infrastructure.LetterText;

namespace Casework.IntegrationTests;

/// <summary>
///     The decision letter comes from the organisation's own template, and the application's
///     one is what it falls back to.
/// </summary>
/// <remarks>
///     <para>
///         <b>Asserted on the document model, never on PDF bytes.</b> A PDF's text is compressed, so
///         searching its bytes finds a name neither when it is there nor when it is not. What the letter <em>says</em> is the resolved
///         <c>DocumentModel</c>; that it can be drawn is a separate, smaller question, and the endpoint
///         answers it.
///     </para>
///     <para>
///         ⚠️ <b>The warning channel is read, and it is what makes this a measurement.</b> A missing
///         property is not an error: the expression resolves to null and the resolver records a
///         <c>TemplateWarning</c>. So a template that names something nobody provides renders a letter
///         with a hole in it, silently, and the only way to know is to look. The last test here is that
///         look — it fails on the warnings rather than on the text.
///     </para>
/// </remarks>
public sealed class TheLetterIsTheOrganisationsOwn(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    /// <summary>Two organisations that write to their applicants differently.</summary>
    private const string Hoy = "hoy";

    private const string Flotta = "flotta";

    /// <summary>…and one that never sent us anything, so it gets the application's letter.</summary>
    private const string Graemsay = "graemsay";

    [Fact]
    public async Task TwoOrganisationsWithTwoTemplatesWriteTwoDifferentLetters()
    {
        await OnboardAsync(IntakeServices, Hoy, null);
        await OnboardAsync(IntakeServices, Flotta, null);

        await UploadAsync(Hoy, LetterThatSays("The Hoy Licensing Board has decided"));
        await UploadAsync(Flotta, LetterThatSays("Flotta Community Council writes to say"));

        var hoysCase = await ACaseOf(Hoy);
        var flottasCase = await ACaseOf(Flotta);

        var hoysLetter = await LetterForAsync(Hoy, hoysCase);
        var flottasLetter = await LetterForAsync(Flotta, flottasCase);

        hoysLetter.FromTheOrganisationsOwnTemplate.Should().BeTrue();
        flottasLetter.FromTheOrganisationsOwnTemplate.Should().BeTrue();

        // The same case, two wordings — and each organisation's letter carries its own case's number,
        // which is what rules out a template that renders somebody else's data.
        TextOf(hoysLetter.Model).Should().Contain("The Hoy Licensing Board has decided");
        TextOf(hoysLetter.Model).Should().NotContain("Flotta Community Council");

        TextOf(flottasLetter.Model).Should().Contain("Flotta Community Council writes to say");
        TextOf(flottasLetter.Model).Should().NotContain("The Hoy Licensing Board");

        TextOf(hoysLetter.Model).Should().Contain(hoysCase.Number);
        TextOf(hoysLetter.Model).Should().NotContain(flottasCase.Number);

        // Neither of them asked for anything the application does not provide.
        hoysLetter.Warnings.Should().BeEmpty();
        flottasLetter.Warnings.Should().BeEmpty();
    }

    /// <summary>
    ///     An organisation that has sent nothing gets the application's letter — and is told so.
    /// </summary>
    /// <remarks>
    ///     The fallback is <b>declared</b>: "the letter rendered" is true of both, and an organisation
    ///     that uploaded a template and is still getting the application's one has a problem that this
    ///     flag is the only way to see.
    /// </remarks>
    [Fact]
    public async Task AnOrganisationWithNoTemplateGetsTheApplicationsOwn()
    {
        await OnboardAsync(IntakeServices, Graemsay, null);

        var theirCase = await ACaseOf(Graemsay);
        var letter = await LetterForAsync(Graemsay, theirCase);

        letter.FromTheOrganisationsOwnTemplate.Should().BeFalse(
            "nothing was uploaded for this organisation, so this is the application's own letter");

        TextOf(letter.Model).Should().Contain("Decision");
        TextOf(letter.Model).Should().Contain(theirCase.Number);
        letter.Warnings.Should().BeEmpty();
    }

    /// <summary>
    ///     The letterhead is a piece of its own: an organisation can replace only that.
    /// </summary>
    [Fact]
    public async Task AnOrganisationCanReplaceOnlyTheLetterhead()
    {
        const string burray = "burray";
        await OnboardAsync(IntakeServices, burray, null);

        await UploadAsync(
            burray,
            """
            <document>
              <page>
                <heading level="2">Burray Parish — Licensing</heading>
                <text>Post Office Buildings, Burray</text>
              </page>
            </document>
            """,
            piece: "header");

        var letter = await LetterForAsync(burray, await ACaseOf(burray));

        TextOf(letter.Model).Should().Contain("Burray Parish — Licensing",
            "the imported partial is this organisation's");
        TextOf(letter.Model).Should().Contain("If you disagree with this decision",
            "and the wording around it is still the application's, which is what a partial is for");
    }

    /// <summary>
    ///     The control: a template that names something nobody provides fails <b>here</b>, through the
    ///     warnings — not in an applicant's letterbox.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this test the suite would be satisfied by a letter with a hole in it: the resolver
    ///     does not throw, the endpoint answers 200, and the PDF has a blank where the reference number
    ///     should be. "The template resolved" and "the data was there" are two different statements, and
    ///     only the second one is worth anything to an applicant.
    /// </remarks>
    [Fact]
    public async Task ATemplateThatAsksForSomethingNobodyProvidesIsReported()
    {
        const string egilsay = "egilsay";
        await OnboardAsync(IntakeServices, egilsay, null);

        await UploadAsync(
            egilsay,
            """
            <document>
              <page>
                <text>Your reference is {{ case.reference }}</text>
              </page>
            </document>
            """);

        var letter = await LetterForAsync(egilsay, await ACaseOf(egilsay));

        letter.Warnings.Should().NotBeEmpty(
            "a property that does not exist resolves to null and is recorded, not thrown");
        letter.Warnings.Select(warning => warning.Path).Should().Contain(path => path.Contains("reference"));

        // And the letter really does have the hole the warning is about — which is what the warning is
        // there to prevent anybody discovering the other way round.
        TextOf(letter.Model).Should().Contain("Your reference is ");
    }

    // ── The letter may quote a table the organisation uploaded ──────────────────────────────────

    /// <summary>
    ///     The amount in the letter comes from the organisation's own spreadsheet, held in storage.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The read <c>Documents.Templating.Spreadsheet</c> existed for and could not do. Every
    ///         entry point of that package took a <b>file path</b>, and what an organisation sends in
    ///         has none: it is behind <c>IFileStorage</c> — local disk here, object storage in a
    ///         deployment. The package could only have been used by reaching around the abstraction,
    ///         and an example that did would be teaching that.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>&lt;for-each&gt;</c> and not <c>fees.Sheets[0].Rows[1]</c>: indexers are
    ///         deliberately unreachable from the expression grammar, so a spreadsheet is read by
    ///         walking it. Measured while writing this — the first version indexed and could not have
    ///         parsed.
    ///     </para>
    ///     <para>
    ///         Measured by removal: taking <c>.AddSource("fees", …)</c> out of the letter turns this
    ///         suite from <b>111</b> into <b>1 failed, 110 passed</b>, and the one red is this case.
    ///         The two controls below stay green on purpose — a negative assertion and a warning about
    ///         a name are both true whether the source is there or not, which is exactly why neither of
    ///         them could have stood in for this one.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheAmountInTheLetterComesFromTheUploadedTable()
    {
        const string westray = "westray";
        await OnboardAsync(IntakeServices, westray, null);

        // ⚠️ Amounts nothing else in this application could produce: if the letter shows them, they
        // were read from these bytes.
        await UploadAsync(westray, "Charge,Amount\r\nLate filing,45.00\r\nCopy of decision,12.50",
            piece: "fee-table");
        await UploadAsync(westray, LetterQuotingTheFeeTable);

        var letter = await LetterForAsync(westray, await ACaseOf(westray));
        var text = TextOf(letter.Model);

        text.Should().Contain("Late filing").And.Contain("45.00",
            "the table is in IFileStorage and has no path — this is the read the file-backed data "
            + "sources could not do");
        text.Should().Contain("Copy of decision").And.Contain("12.50");

        letter.Warnings.Should().BeEmpty("the template asked for nothing the table does not have");
    }

    /// <summary>
    ///     The first control: without an upload there are no amounts, and the letter still renders.
    /// </summary>
    /// <remarks>
    ///     Without it, "the letter contains 45.00" is satisfied by a letter that contains it for any
    ///     reason — a default, a fixture, a schedule the application shipped. Here the same template
    ///     renders against no table, and the only difference is the upload.
    /// </remarks>
    [Fact]
    public async Task WithNoTableUploaded_TheLetterQuotesNoAmounts()
    {
        const string papay = "papay";
        await OnboardAsync(IntakeServices, papay, null);

        await UploadAsync(papay, LetterQuotingTheFeeTable);

        var letter = await LetterForAsync(papay, await ACaseOf(papay));

        TextOf(letter.Model).Should().NotContain("45.00",
            "nothing was uploaded for this organisation, so there is no amount to quote");
        TextOf(letter.Model).Should().Contain("Schedule of charges",
            "and the rest of the letter still renders — a missing source is a hole, not a failure");
    }

    /// <summary>
    ///     The second control: naming something the table does not have is <b>reported</b>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control the decision asked for by name. A template quoting a column nobody supplies
    ///     renders a blank where an amount should be, the endpoint answers 200, and an applicant
    ///     receives it.
    /// </remarks>
    [Fact]
    public async Task ATemplateNamingSomethingTheTableDoesNotHaveIsReported()
    {
        const string sanday = "sanday";
        await OnboardAsync(IntakeServices, sanday, null);

        await UploadAsync(sanday, "Charge,Amount\r\nLate filing,45.00", piece: "fee-table");
        await UploadAsync(
            sanday,
            """
            <document title="Decision" page-size="A4">
              <page>
                <text>Total due: {{ fees.Total }}</text>
              </page>
            </document>
            """);

        var letter = await LetterForAsync(sanday, await ACaseOf(sanday));

        letter.Warnings.Should().NotBeEmpty(
            "a name the spreadsheet does not answer resolves to null and is recorded, not thrown");
        letter.Warnings.Select(warning => warning.Path).Should().Contain(path => path.Contains("Total"));

        TextOf(letter.Model).Should().Contain("Total due: ");
    }

    /// <summary>A letter that walks the uploaded table.</summary>
    private const string LetterQuotingTheFeeTable =
        """
        <document title="Decision" page-size="A4">
          <page>
            <heading level="1">Decision</heading>
            <text>Case {{ case.number }}</text>
            <heading level="2">Schedule of charges</heading>
            <for-each source="fees.Sheets" item="sheet">
              <for-each source="sheet.Rows" item="row">
                <for-each source="row.Cells" item="cell">
                  <text>{{ cell.Value }}</text>
                </for-each>
              </for-each>
            </for-each>
          </page>
        </document>
        """;

    /// <summary>
    ///     One organisation's letter, with its own opening line and the shared placeholders.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>$$$"""</c>: with two dollars the interpolation delimiter is <c>{{ }}</c>, which is
    ///     exactly the template's own syntax — so <c>{{ case.number }}</c> is read by the C# compiler
    ///     instead of by the template engine, and <c>case</c> is a keyword. Three dollars moves the
    ///     compiler out of the way.
    /// </remarks>
    private static string LetterThatSays(string opening) =>
        $$$"""
          <document title="Decision" page-size="A4">
            <page>
              <heading level="1">{{{opening}}}</heading>
              <text>Case {{ case.number }} — {{ case.subject }}</text>
              <text>Applicant: {{ applicant.name | default:"—" }}</text>
            </page>
          </document>
          """;

    /// <summary>Sends one piece of a letter in, as that organisation, through the real endpoint.</summary>
    /// <remarks>
    ///     ⚠️ The extension and content type follow the piece, because the fee table is the one piece
    ///     that is not markup: a <c>.csv</c> declared as <c>text/csv</c>, which the action's
    ///     <c>[AllowedContentTypes]</c> had to learn.
    /// </remarks>
    private async Task UploadAsync(string tenantId, string markup, string piece = "decision-letter")
    {
        var isTable = piece == "fee-table";

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(markup));
        file.Headers.ContentType = new MediaTypeHeaderValue(isTable ? "text/csv" : "application/xml");

        form.Add(file, "file", $"{piece}{(isTable ? ".csv" : ".pdxdoc")}");
        form.Add(new StringContent(piece), "piece");

        var sent = await IntakeAs(Caseworker, tenantId).PostAsync("api/letter-templates", form);

        sent.IsSuccessStatusCode.Should().BeTrue(
            "{0}", await sent.Content.ReadAsStringAsync());
    }

    /// <summary>A decided case of one organisation, opened through the API.</summary>
    private async Task<(Guid Id, string Number)> ACaseOf(string tenantId)
    {
        var opened = await ReadJsonAsync(await IntakeAs(Caseworker, tenantId).PostAsJsonAsync(
            "api/cases",
            new
            {
                subject = $"A licence for a shop in {tenantId}",
                applicant = "A. Applicant",
                applicantEmail = "a.applicant@example.org"
            }));

        return (opened.GetProperty("id").GetGuid(), opened.GetProperty("number").GetString()!);
    }

    /// <summary>
    ///     The letter as the application builds it, inside that organisation's tenant.
    /// </summary>
    /// <remarks>
    ///     Through the service and not the endpoint, because what is asserted is the <b>model</b>. The
    ///     endpoint's own answer — that it renders to PDF at all — is
    ///     <see cref="TheLetterRendersToPdf" />.
    /// </remarks>
    private async Task<WrittenLetter> LetterForAsync(string tenantId, (Guid Id, string Number) theCase)
        => await AsTenantAsync(
            async () =>
            {
                using var scope = IntakeServices.CreateScope();

                return await scope.ServiceProvider
                    .GetRequiredService<IWriteTheDecisionLetter>()
                    .ForCaseAsync(theCase.Id);
            },
            tenantId);

    /// <summary>
    ///     And that the model can be drawn: the endpoint answers with a PDF.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The assertion is the signature and the length, not the text: a PDF's content stream is
    ///     compressed, so looking for a word in its bytes answers "no" whether or not the word is in the
    ///     document. What the letter says is asserted on the model, above.
    /// </remarks>
    [Fact]
    public async Task TheLetterRendersToPdf()
    {
        const string wyre = "wyre";
        await OnboardAsync(IntakeServices, wyre, null);

        var theirCase = await ACaseOf(wyre);

        var response = await IntakeAs(Caseworker, wyre).GetAsync($"api/cases/{theirCase.Id}/letter");

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        bytes.Length.Should().BeGreaterThan(500);
    }
}
