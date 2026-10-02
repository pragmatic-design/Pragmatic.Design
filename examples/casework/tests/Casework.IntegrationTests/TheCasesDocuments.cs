using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Casework.IntegrationTests.Infrastructure;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     A case's documents: uploaded once, kept under a key the case decides, and served back
///     by their hash.
/// </summary>
/// <remarks>
///     <para>
///         The bytes are asserted, not the fact that a 200 came back: a download that returns a document
///         is only a document if it is the same one that went up. The hash is asserted on both sides —
///         the one the application recorded and the one this test computes — so a store that returned
///         somebody else's file of the right length would fail.
///     </para>
///     <para>
///         The second case reading the first one's document is <b>404</b>, and the reason it is not 403
///         is worth keeping: a 403 confirms the document exists, which is the fact the caller was not
///         allowed to learn.
///     </para>
/// </remarks>
public sealed class TheCasesDocuments(PostgresFixture databases, RabbitMqFixture broker)
    : CaseworkTestBase(databases, broker)
{
    private const string Caseworker = "caseworker";

    private static readonly byte[] TheScan = [0x25, 0x50, 0x44, 0x46, .. "a scanned identity card"u8];

    [Fact]
    public async Task ADocumentGoesUpOnceAndComesBackAsTheSameBytes()
    {
        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        var uploaded = await operator1.PostAsync($"api/cases/{id}/documents", AScan());
        var body = await uploaded.Content.ReadAsStringAsync();
        uploaded.IsSuccessStatusCode.Should().BeTrue($"the document is accepted: {body}");

        var document = await ReadJsonAsync(uploaded);
        var documentId = document.GetProperty("id").GetGuid();
        document.GetProperty("fileName").GetString().Should().Be("identity-card.pdf",
            "the name the applicant's file had, which is what an operator recognises it by");
        document.GetProperty("length").GetInt64().Should().Be(TheScan.Length,
            "read through, so the length is what arrived and not what the header claimed");
        document.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(TheScan)),
            "the hash of the bytes, which is what makes the download verifiable");

        var download = await operator1.GetAsync($"api/cases/{id}/documents/{documentId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the document is served back: {await download.Content.ReadAsStringAsync()}");

        (await download.Content.ReadAsByteArrayAsync()).Should().BeEquivalentTo(TheScan,
            "the same bytes, byte for byte");
        download.Headers.ETag?.Tag.Trim('"').Should().Be(Convert.ToHexString(SHA256.HashData(TheScan)),
            "the ETag is the hash: a client that has these bytes can say so");
    }

    /// <summary>
    ///     A document belongs to its case, and a case that is not its case does not have it.
    /// </summary>
    [Fact]
    public async Task AnotherCasesDocumentIsNotFound()
    {
        var operator1 = IntakeAs(Caseworker);
        var mine = await ACaseAsync(operator1);
        var theirs = await ACaseAsync(operator1);

        var uploaded = await ReadJsonAsync(await operator1.PostAsync($"api/cases/{mine}/documents", AScan()));
        var documentId = uploaded.GetProperty("id").GetGuid();

        var stolen = await operator1.GetAsync($"api/cases/{theirs}/documents/{documentId}");

        stolen.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "404 and not 403: a 403 confirms the document exists, which is what the caller was not allowed to learn");
    }

    /// <summary>
    ///     The shape of the storage key, so changing it is a decision and not an accident.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The key is read from the column, because it is deliberately not on the wire: a client that
    ///         could see a key is a client that could compose another one. What the assertion is really
    ///         about is the <b>tenant</b> being in it — file storage has no tenant filter, so the key is
    ///         the only thing keeping one organisation's documents out of another's container — and the
    ///         file's name <b>not</b> being the one the caller sent, which is what makes a name from a
    ///         client unable to become part of an address.
    ///     </para>
    ///     <para>
    ///         And no backslash anywhere in it: the gate runs on Windows and CI runs on ubuntu, where a
    ///         <c>\</c> is an ordinary character in a file name rather than a separator. A key composed
    ///         with <c>Path.Combine</c> would pass here and produce one flat file per case there.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheStorageKeyCarriesTheTenantAndTheCase()
    {
        var operator1 = IntakeAs(Caseworker);
        var id = await ACaseAsync(operator1);

        await operator1.PostAsync($"api/cases/{id}/documents", AScan());

        var key = (string)(await ScalarAsync(
            IntakeConnectionString,
            $"""select "StorageKey" from "CaseDocuments" where "CaseId" = '{id}'"""))!;

        key.Should().Contain($"cases/{Tenant}/{id}/",
                "the container is the case, inside its organisation")
            .And.EndWith(".pdf", "the extension survives, which is what the store reads the name for")
            .And.NotContain("identity-card",
                "the name the caller sent is data on the row and never part of the address")
            .And.NotContain("\\", "a key is composed with '/' on every platform");

        // And the bytes are where the key says they are: the store's root, plus the key.
        var stored = Path.GetFileName(key);
        Directory.EnumerateFiles(IntakeStorageRoot, "*", SearchOption.AllDirectories)
            .Should().Contain(path => path.EndsWith(stored, StringComparison.Ordinal),
                $"the file is under {IntakeStorageRoot}, at the key the row records");
    }

    private static async Task<Guid> ACaseAsync(HttpClient caller)
    {
        var created = await ReadJsonAsync(await caller.PostAsJsonAsync("api/cases", new
        {
            subject = "A licence for a food stall",
            applicant = "A. Applicant"
        }));

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>The upload, as a client sends it: the field name is the <c>[FromForm]</c> property's.</summary>
    private static MultipartFormDataContent AScan()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TheScan);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "File", "identity-card.pdf");

        return form;
    }
}
