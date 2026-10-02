using Pragmatic.Endpoints.Responses;
using Pragmatic.Storage;

namespace Casework.Intake.Cases.Endpoints;

/// <summary>
///     Serves a case's document back: the very bytes that were uploaded, with their hash as the ETag.
/// </summary>
/// <remarks>
///     <para>
///         <b>Two reads and both are necessary.</b> The case is fetched through the tenant-filtered
///         repository, so a case of another organisation is not found; then the document is fetched by
///         <em>its</em> id <b>and</b> the case's, so a document that exists but belongs to another case
///         is not found either. Dropping the first would serve another organisation's document to
///         anybody who knew two ids; dropping the second would serve any document to anybody who owned
///         one case.
///     </para>
///     <para>
///         ⚠️ <b>404 and never 403.</b> A 403 answers the question "does this document exist" for a
///         caller who was not allowed to ask it. The same status for "no such document" and "not yours"
///         is what keeps the two indistinguishable from outside.
///     </para>
///     <para>
///         The key is read back with <c>UriKind.RelativeOrAbsolute</c>: what the store returned is
///         provider-defined, and the absolute-only <c>new Uri(text)</c> throws on local disk's relative
///         shape.
///     </para>
/// </remarks>
[Endpoint(HttpVerb.Get, "api/cases/{caseId}/documents/{id}")]
[RequirePermission(IntakePermissions.Case.Read)]
public partial class DownloadCaseDocumentEndpoint : Endpoint<FileResponse, NotFoundError>
{
    private IReadRepository<Case> _cases = null!;
    private IReadRepository<CaseDocument> _documents = null!;
    private IFileStorage _files = null!;

    public required Guid CaseId { get; init; }

    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        var @case = await _cases.GetByIdAsync(CaseId, ct).ConfigureAwait(false);
        if (@case is null)
            return NotFoundError.For<Guid>("CaseDocument", Id);

        var document = await _documents
            .FirstOrDefaultAsync(CaseDocumentSpecifications.OfCase(@case.Id, Id), ct)
            .ConfigureAwait(false);
        if (document is null)
            return NotFoundError.For<Guid>("CaseDocument", Id);

        var content = await _files
            .GetAsync(new Uri(document.StorageKey, UriKind.RelativeOrAbsolute), ct)
            .ConfigureAwait(false);
        if (content is null)
            return NotFoundError.For<Guid>("CaseDocument", Id);

        return new FileResponse(content, document.ContentType, document.FileName)
        {
            ETag = document.Sha256,
        };
    }
}
