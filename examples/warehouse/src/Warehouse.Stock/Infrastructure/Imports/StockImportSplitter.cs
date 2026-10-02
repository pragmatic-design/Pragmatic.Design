using Pragmatic.Messaging.Batch;
using Warehouse.Stock.Imports.Messages;

namespace Warehouse.Stock.Infrastructure.Imports;

/// <summary>
///     The parts of an import, in order. The file is cut where it is read (<c>StartImportAction</c>),
///     because the part size and each row's line number are known there.
/// </summary>
/// <remarks>Deterministic, as <see cref="BatchDispatcher{TBatch,TItem}.ResumeAsync" /> requires of a split.</remarks>
public sealed class StockImportSplitter : IBatchSplitter<StockImport, ImportFilePart>
{
    public IReadOnlyList<ImportFilePart> Split(StockImport batch) => batch.Parts;
}
