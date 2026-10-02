using System.Globalization;
using Pragmatic.Caching;
using Warehouse.Stock.Imports.Messages;
using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Imports.Actions;

/// <summary>
///     Applies one part of a supplier's file: each valid row becomes a receipt, each invalid one is
///     recorded with its reason, and the part is recorded as applied.
/// </summary>
/// <remarks>
///     <para>
///         All in one transaction — the receipts, the refusals and the part's record — so a part is either
///         applied whole or not at all, and a second delivery finds the record and writes nothing
///         (<see cref="ImportedPart" />).
///     </para>
///     <para>
///         A row is checked here and not when the file arrives: the SKU and the location have to exist,
///         and this is where the catalogue is read. An invalid row does not stop the valid ones.
///     </para>
///     <para>
///         No <c>[Endpoint]</c>; run by <c>ApplyImportParts</c> for each part on the broker.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.ImportedPart.Create)]
public partial class ApplyImportPartAction : DomainAction<ImportPartOutcomeDto>, ICacheInvalidator
{
    private IRepository<ImportedPart> _parts = null!;
    private IRepository<ImportRejection> _rejections = null!;
    private IReadRepository<Product> _products = null!;
    private IReadRepository<Location> _locations = null!;
    private IRepository<StockLevel> _levels = null!;
    private IRepository<StockMovement> _movements = null!;
    private readonly ISet<Guid> _moved = new HashSet<Guid>();

    public required Guid ImportId { get; init; }

    public required int PartIndex { get; init; }

    public required List<ImportFileRow> Rows { get; init; }

    public override async Task<Result<ImportPartOutcomeDto, IError>> Execute(CancellationToken ct = default)
    {
        // Already applied: the answer is what the part did then, so a repeat is counted as the part was.
        var earlier = await _parts
            .FirstOrDefaultAsync(Spec<ImportedPart>.Where(p => p.ImportId == ImportId && p.PartIndex == PartIndex), ct)
            .ConfigureAwait(false);
        if (earlier is not null)
            return new ImportPartOutcomeDto { AlreadyApplied = true, Applied = earlier.Applied, Rejected = earlier.Rejected };

        var skus = Rows.Select(row => row.Sku).Distinct().ToList();
        var codes = Rows.Select(row => row.Location).Distinct().ToList();
        var products = (await _products.FindAsync(Spec<Product>.Where(p => skus.Contains(p.Sku)), ct).ConfigureAwait(false))
            .ToDictionary(p => p.Sku, p => p.PersistenceId);
        var locations = (await _locations.FindAsync(Spec<Location>.Where(l => codes.Contains(l.Code)), ct).ConfigureAwait(false))
            .ToDictionary(l => l.Code, l => l.PersistenceId);

        // One level per product and location for the whole part: a level created by an earlier row is not
        // in the database yet, and looking it up again would create a second one.
        var levels = new Dictionary<(Guid, Guid), StockLevel>();
        var applied = 0;
        var rejected = 0;
        foreach (var row in Rows)
        {
            var refused = WhyRefused(row, products, locations, out var quantity);
            if (refused is not null)
            {
                _rejections.Add(ImportRejection.Of(ImportId, PartIndex, row.Line, row.Sku, refused));
                rejected++;
                continue;
            }

            var key = (products[row.Sku], locations[row.Location]);
            if (!levels.TryGetValue(key, out var level))
            {
                level = await _levels.FirstOrDefaultAsync(StockLevelSpecifications.Of(key.Item1, key.Item2), ct)
                    .ConfigureAwait(false);
                if (level is null)
                {
                    level = StockLevel.Empty(key.Item1, key.Item2);
                    _levels.Add(level);
                }

                levels[key] = level;
            }

            _movements.Add(level.Receive(quantity));
            _moved.Add(key.Item1);
            applied++;
        }

        _parts.Add(ImportedPart.Of(ImportId, PartIndex, applied, rejected));
        return new ImportPartOutcomeDto { Applied = applied, Rejected = rejected };
    }

    /// <summary>Drops the availability of the products this part received, and of no other.</summary>
    public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
        => AvailabilityCache.DropAsync(cache, _moved, ct);

    /// <summary>Why a row cannot be applied, or null when it can.</summary>
    private static string? WhyRefused(
        ImportFileRow row, Dictionary<string, Guid> products, Dictionary<string, Guid> locations, out int quantity)
    {
        if (!int.TryParse(row.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
            return $"quantity '{row.Quantity}' is not a positive whole number";
        if (!products.ContainsKey(row.Sku))
            return $"no product with SKU '{row.Sku}'";
        if (!locations.ContainsKey(row.Location))
            return $"no location '{row.Location}'";

        return null;
    }
}
