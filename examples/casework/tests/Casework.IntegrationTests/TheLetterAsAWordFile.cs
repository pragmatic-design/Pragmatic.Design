using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The same decision letter, rendered a second way.
/// </summary>
/// <remarks>
///     <para>
///         <c>DownloadTheDecisionLetterEndpoint</c>'s remark says the format is chosen at the
///         endpoint and nowhere earlier, because the service answers with a <c>DocumentModel</c>.
///         Every caller of that service chose PDF, so the claim had one witness and could not fail.
///         A second endpoint over the same model is the demonstration — and a Word file is the
///         deliverable a caseworker asks for, because a decision that has to be amended before it
///         goes out cannot be amended in a PDF.
///     </para>
///     <para>
///         ⚠️ <b>The text is asserted here, unlike in the PDF test.</b> A PDF's content stream is
///         compressed, so searching its bytes answers "no" whether or not the word is there — which
///         is why <c>TheLetterRendersToPdf</c> checks only the signature and the length. A DOCX is a
///         ZIP whose <c>word/document.xml</c> is plain XML, so the letter's words really can be read
///         out of the file, and that is the stronger assertion this format allows.
///     </para>
/// </remarks>
public sealed class TheLetterAsAWordFile(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task TheWordFile_IsAnOoxmlPackageAndSaysWhatTheLetterSays()
    {
        const string sanday = "sanday";
        await OnboardAsync(IntakeServices, sanday, null);

        var theirCase = await ACaseOf(sanday);

        var response = await IntakeAs(Caseworker, sanday).GetAsync($"api/cases/{theirCase.Id}/letter.docx");

        response.IsSuccessStatusCode.Should().BeTrue("{0}", await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType?.MediaType.Should().Be(DocxContentType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 2).Should().Be("PK", "a DOCX is a ZIP package");

        DocumentXmlOf(bytes).Should().Contain(theirCase.Number,
            "the letter names the case it decides, and in this format that is readable in the file");
    }

    /// <summary>
    ///     The control: two formats, one letter — and the two answers are genuinely different files.
    /// </summary>
    /// <remarks>
    ///     Without it, "a DOCX came back" is satisfied by an endpoint that renders the PDF and labels
    ///     it differently, which is the mistake a copied endpoint makes and which no assertion about
    ///     the content type can catch.
    /// </remarks>
    [Fact]
    public async Task ThePdfAndTheWordFile_AreTheSameLetterInTwoFormats()
    {
        const string eday = "eday";
        await OnboardAsync(IntakeServices, eday, null);

        var theirCase = await ACaseOf(eday);
        var client = IntakeAs(Caseworker, eday);

        var pdf = await (await client.GetAsync($"api/cases/{theirCase.Id}/letter"))
            .Content.ReadAsByteArrayAsync();
        var word = await (await client.GetAsync($"api/cases/{theirCase.Id}/letter.docx"))
            .Content.ReadAsByteArrayAsync();

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Encoding.ASCII.GetString(word, 0, 2).Should().Be("PK");

        DocumentXmlOf(word).Should().Contain(theirCase.Number,
            "and both are about this case: the model is the same one, rendered twice");
    }

    /// <summary>The <c>word/document.xml</c> part of an OOXML package, as text.</summary>
    private static string DocumentXmlOf(byte[] docx)
    {
        using var package = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);

        var part = package.GetEntry("word/document.xml");
        part.Should().NotBeNull("a wordprocessing package keeps its body there");

        using var reader = new StreamReader(part!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A case of one organisation, opened through the API.</summary>
    private async Task<(Guid Id, string Number)> ACaseOf(string tenantId)
    {
        var response = await IntakeAs(Caseworker, tenantId).PostAsJsonAsync(
            "api/cases",
            new
            {
                subject = $"A licence for a shop in {tenantId}",
                applicant = "A. Applicant",
                applicantEmail = "a.applicant@example.org"
            });

        response.IsSuccessStatusCode.Should().BeTrue("{0}", await response.Content.ReadAsStringAsync());

        var opened = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (opened.GetProperty("id").GetGuid(), opened.GetProperty("number").GetString()!);
    }
}
