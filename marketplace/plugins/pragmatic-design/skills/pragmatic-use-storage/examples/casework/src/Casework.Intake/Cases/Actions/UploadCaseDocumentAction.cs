using System.Security.Cryptography;
using Casework.Intake.Cases;
using Microsoft.AspNetCore.Http;
using Pragmatic.Storage;

namespace Casework.Intake.Cases.Actions;

/// <summary>
///     Attaches a document to a case: the bytes go to the store, the facts about them go on the case.
/// </summary>
/// <remarks>
///     <para>
///         An action and not a mutation, because the input is a file: a mutation maps its properties onto
///         the entity by name, and a stream is not a column. What lands on the case is what can be
///         written down about the bytes — name, type, length, hash, key — and the bytes themselves go
///         where bytes go.
///     </para>
///     <para>
///         The case is loaded through <c>[LoadEntity]</c>, which means through the ordinary
///         tenant-filtered repository: a case of another organisation is not found, so this operation
///         cannot attach anything to it. The permission is the case's own <c>update</c> — attaching a
///         document is changing the case, and a permission of its own would be a second answer to the
///         same question.
///     </para>
///     <para>
///         ⚠️ The bytes are written to the store before the transaction commits, so a failed commit
///         leaves a file nobody references. Deliberate, and the same choice Invoicing made: the
///         alternative — storing after the commit, from a handler — is a second write to the row just
///         written, with a failure that has nowhere to be reported. An unreferenced blob is garbage; a
///         row pointing at bytes that were never written is a 500 on download.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(IntakePermissions.Case.Update)]
[Endpoint(HttpVerb.Post, "api/cases/{id}/documents")]
[LoadEntity<Case>(nameof(Id), FieldName = "_case")]
public partial class UploadCaseDocumentAction : DomainAction<CaseDocumentDto, IError>
{
    // _case is declared by the generator from [LoadEntity(FieldName = "_case")]: declaring it here too
    // is CS0102, and a concrete type in an action's field is PRAG0419 — the loaded row is not a service.
    private IFileStorage _files = null!;

    public required Guid Id { get; init; }

    /// <summary>The file itself, bound from <c>multipart/form-data</c>.</summary>
    /// <remarks>
    ///     The size and the accepted types are <b>declared</b> rather than checked in the body below:
    ///     the generated endpoint refuses what it must before this code runs, which is the difference
    ///     between a limit and a hope. 10 MB is this example's number, not the framework's.
    /// </remarks>
    [FromForm]
    [MaxFileSize(10 * 1024 * 1024)]
    [AllowedContentTypes("application/pdf", "image/jpeg", "image/png")]
    public required IFormFile File { get; init; }

    /// <summary>When it arrived: the application's clock, never the caller's.</summary>
    [FromClock]
    public DateTimeOffset UploadedOn { get; private set; }

    public override async Task<Result<CaseDocumentDto, IError>> Execute(CancellationToken ct = default)
    {
        // Read through into memory once: the bytes have to be hashed and stored, and a request stream
        // cannot be read twice. 10 MB is the declared ceiling above, which is what makes buffering here
        // a bounded decision rather than an open one.
        using var buffered = new MemoryStream();
        var upload = File.OpenReadStream();
        await using (upload.ConfigureAwait(false))
            await upload.CopyToAsync(buffered, ct).ConfigureAwait(false);

        var bytes = buffered.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));

        // The uploaded name goes to the store as itself: of it the store keeps the extension and names
        // the file with a Guid of its own. What this operation decides is the container.
        buffered.Position = 0;
        var stored = await _files.SaveAsync(
                buffered,
                File.FileName,
                CaseDocumentStorageKey.ContainerFor(_case.TenantId, _case.Id),
                ct)
            .ConfigureAwait(false);

        // The length is what arrived, counted by the copy above — not File.Length, which is what the
        // client's header claimed.
        var document = _case.Attach(
            File.FileName, File.ContentType, bytes.Length, sha256, stored.ToString(), UploadedOn);

        return CaseDocumentDto.FromEntity(document);
    }
}
