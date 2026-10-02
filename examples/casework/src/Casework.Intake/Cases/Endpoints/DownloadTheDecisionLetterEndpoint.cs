using Casework.Intake.Infrastructure.Letters;
using Pragmatic.Documents.Pdf;
using Pragmatic.Endpoints.Responses;

namespace Casework.Intake.Cases.Endpoints;

/// <summary>
///     The decision letter of a case, as a PDF.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This is where the format is chosen, and nowhere earlier.</b> The service builds a
///         <c>DocumentModel</c> — what the letter says — and this endpoint is the place that wanted
///         bytes, so it is the place that names a renderer. A service that returned PDF would have
///         decided for the decision mail as well, and for every other caller after it.
///     </para>
///     <para>
///         The case is read through the tenant-filtered repository inside the service, so a case of
///         another organisation is not found and there is no letter to render — 404, like every other
///         "not yours" in this module.
///     </para>
///     <para>
///         ⚠️ The letter's <c>Warnings</c> are <b>not</b> inspected here, and that is deliberate: a
///         letter with a hole in it is still the letter this organisation's template asked for, and an
///         endpoint that refused to serve it would be deciding for the organisation. What reads them is
///         the test, which is where a template that names something nobody provides has to fail.
///     </para>
/// </remarks>
[Endpoint(HttpVerb.Get, "api/cases/{id}/letter")]
[RequirePermission(IntakePermissions.Case.Read)]
public partial class DownloadTheDecisionLetterEndpoint : Endpoint<FileResponse, NotFoundError>
{
    private IReadRepository<Case> _cases = null!;
    private IWriteTheDecisionLetter _letters = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        if (await _cases.GetByIdAsync(Id, ct).ConfigureAwait(false) is null)
            return NotFoundError.For<Guid>("Case", Id);

        var letter = await _letters.ForCaseAsync(Id, ct).ConfigureAwait(false);
        var pdf = await PdfRenderer.RenderAsync(letter.Model, ct: ct).ConfigureAwait(false);

        return new FileResponse(new MemoryStream(pdf), "application/pdf", $"decision-{Id}.pdf");
    }
}
