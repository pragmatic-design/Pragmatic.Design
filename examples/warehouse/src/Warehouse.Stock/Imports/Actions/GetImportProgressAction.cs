using Pragmatic.Messaging.Batch;

namespace Warehouse.Stock.Imports.Actions;

/// <summary>
///     How far an import has got: the batch's counts, which parts failed, and every row refused with its
///     reason.
/// </summary>
/// <remarks>
///     <para>
///         Two sources, each saying what it knows. The progress store (<c>__BatchProgress</c>, from
///         <c>[EnableBatchProgress]</c>) counts parts done and failed as they are handled, whichever instance
///         handled them. The parts' own records say which parts had refused rows, and the refusals say why.
///     </para>
///     <para>
///         An action and not a query: the answer is read from the batch progress store, which is not an
///         entity of this module.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.ImportedPart.Read)]
[Endpoint(HttpVerb.Get, "api/imports/{id}")]
public partial class GetImportProgressAction : DomainAction<ImportProgressDto, NotFoundError>
{
    private IBatchProgressStore _progress = null!;
    private IReadRepository<ImportedPart> _parts = null!;
    private IReadRepository<ImportRejection> _rejections = null!;

    public required Guid Id { get; init; }

    public override async Task<Result<ImportProgressDto, IError>> Execute(CancellationToken ct = default)
    {
        var progress = await _progress.GetProgressAsync(Id, ct).ConfigureAwait(false);
        if (progress is null)
            return NotFoundError.Create("Import", Id.ToString());

        var failedParts = await _parts
            .FindAsync(Spec<ImportedPart>.Where(p => p.ImportId == Id && p.Rejected > 0), ct)
            .ConfigureAwait(false);
        var rejections = await _rejections
            .FindAsync(Spec<ImportRejection>.Where(r => r.ImportId == Id), ct)
            .ConfigureAwait(false);

        return new ImportProgressDto
        {
            ImportId = Id,
            Parts = progress.Total,
            Done = progress.Completed,
            Failed = progress.Failed,
            Finished = progress.IsComplete,
            FailedParts = [.. failedParts.Select(p => p.PartIndex).Order()],
            RejectedRows = [.. rejections.OrderBy(r => r.Line).Select(ImportRejectionDto.FromEntity)],
        };
    }
}
