using Casework.Intake.Infrastructure.Letters;
using Pragmatic.Documents.Docx;
using Pragmatic.Endpoints.Responses;

namespace Casework.Intake.Cases.Endpoints;

/// <summary>
///     The same decision letter, as a Word file.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This endpoint exists to make a claim true.</b>
///         <see cref="DownloadTheDecisionLetterEndpoint" /> says the format is chosen at the endpoint
///         and nowhere earlier, because the service answers with a <c>DocumentModel</c> — and until
///         now every caller of that service chose PDF, so the claim was an intention with one
///         witness. Two renderers over one model is the demonstration.
///     </para>
///     <para>
///         And it is the deliverable a caseworker actually asks for: a decision that has to be
///         amended before it goes out cannot be amended in a PDF. The letter is the organisation's
///         own template either way, warnings and all — this endpoint decides the bytes, nothing else.
///     </para>
/// </remarks>
[Endpoint(HttpVerb.Get, "api/cases/{id}/letter.docx")]
[RequirePermission(IntakePermissions.Case.Read)]
public partial class DownloadTheDecisionLetterAsWordEndpoint : Endpoint<FileResponse, NotFoundError>
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private IReadRepository<Case> _cases = null!;
    private IWriteTheDecisionLetter _letters = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        if (await _cases.GetByIdAsync(Id, ct).ConfigureAwait(false) is null)
            return NotFoundError.For<Guid>("Case", Id);

        var letter = await _letters.ForCaseAsync(Id, ct).ConfigureAwait(false);
        var docx = await DocxRenderer.RenderAsync(letter.Model, ct: ct).ConfigureAwait(false);

        return new FileResponse(new MemoryStream(docx), DocxContentType, $"decision-{Id}.docx");
    }
}
