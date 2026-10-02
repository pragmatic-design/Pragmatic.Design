using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Showcase.Catalog.Properties.Endpoints;

/// <summary>
/// Imports seasonal rate adjustments for multiple room types in one HTTP call.
/// Demonstrates:
/// - Bulk pattern: ONE query to load all target RoomTypes (vs N individual GetByIdAsync)
/// - Batch write: all updates committed in a single SaveChangesAsync (via keyed IUnitOfWork)
/// - [RequirePermission] on a write endpoint — only users with "rates.import" can call this
/// - Partial success: items not found or with invalid rates are skipped; TotalSkipped is returned
/// </summary>
/// <remarks>
/// ⚠️ <c>[HttpStatus(200)]</c> because the default is wrong here. A POST with a body defaults to
/// <b>201 Created</b> — the generator has no way to know better — and this operation creates
/// nothing: it updates rates that already exist. A 201 with an empty <c>Location</c> would tell
/// a client to follow a header that is not there.
/// </remarks>
[Endpoint(HttpVerb.Post, "/{propertyId}/rates/import")]
[EndpointGroup<PropertiesGroup>]
[HttpStatus(Microsoft.AspNetCore.Http.StatusCodes.Status200OK)]
[ApiSummary("Import Seasonal Rates")]
[ApiTags("Properties")]
[Pragmatic.Authorization.RequirePermission("rates.import")]
public partial class ImportSeasonalRatesEndpoint : Endpoint<ImportRatesResult>
{
    private IRepository<RoomType> _roomTypes = null!;

    /// <summary>
    /// IServiceProvider used to resolve the keyed IUnitOfWork for CatalogBoundary.
    /// Required because IUnitOfWork is registered as a keyed service per boundary
    /// (GetRequiredKeyedService&lt;IUnitOfWork&gt;(typeof(CatalogBoundary))).
    /// </summary>
    private IServiceProvider _serviceProvider = null!;

    [FromRoute]
    public Guid PropertyId { get; set; }

    [FromBody]
    public required SeasonalRateImportRequest[] Rates { get; init; }

    public override async Task<Result<ImportRatesResult>> HandleAsync(CancellationToken ct = default)
    {
        if (Rates.Length == 0)
            return new ImportRatesResult { TotalSubmitted = 0, TotalUpdated = 0, TotalSkipped = 0 };

        // Bulk load: one round-trip to fetch all requested RoomTypes for this property
        // instead of N individual GetByIdAsync calls in a loop.
        // ⚠️ PersistenceId, not Id: Id is the entity's convenience face and is not a mapped column,
        // so EF cannot translate it and the whole endpoint answered 500. Nothing noticed because the
        // only test of this route asserts the 403 it gets without the permission, which never
        // reaches the body.
        var requestedIds = Rates.Select(r => r.RoomTypeId).ToHashSet();
        var roomTypes = await _roomTypes.Query()
            .Where(rt => rt.PropertyId == PropertyId && requestedIds.Contains(rt.PersistenceId))
            .ToDictionaryAsync(rt => rt.PersistenceId, ct)
            .ConfigureAwait(false);

        var updated = 0;
        var skipped = 0;

        foreach (var rate in Rates)
        {
            if (!roomTypes.TryGetValue(rate.RoomTypeId, out var roomType)
                || rate.NewBaseRate <= 0)
            {
                skipped++;
                continue;
            }

            roomType.SetBaseRate(rate.NewBaseRate);
            updated++;
        }

        // Batch save: all updates committed in ONE SaveChangesAsync call
        // Uses keyed IUnitOfWork for CatalogBoundary to ensure correct DbContext scoping
        if (updated > 0)
        {
            var unitOfWork = _serviceProvider
                .GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary));

            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new ImportRatesResult
        {
            TotalSubmitted = Rates.Length,
            TotalUpdated = updated,
            TotalSkipped = skipped
        };
    }
}
