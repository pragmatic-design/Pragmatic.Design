using Microsoft.Extensions.Options;
using Pragmatic.Caching;
using Pragmatic.Jobs;
using Pragmatic.Temporal.Clock;
using Warehouse.Stock.Contracts;
using Warehouse.Stock.Infrastructure.Caching;
using Warehouse.Stock.Infrastructure.Configuration;
using Warehouse.Stock.Infrastructure.FeatureFlags;
using Warehouse.Stock.Infrastructure.Jobs;

namespace Warehouse.Stock.Reservations.Actions;

/// <summary>
///     Holds what an order needs — every line, or none of them — and says, line by line, what was there.
///     With <see cref="AcceptBackordersFlag" /> switched on, a short order is held for what there is and the
///     rest of each line is backordered instead.
/// </summary>
/// <remarks>
///     <para>
///         The switch is read at each request, through the Agent: whichever Stock instance the request
///         reaches answers the same way, and turning it off restores all-or-nothing without a restart.
///     </para>
///     <para>
///         No <c>[Endpoint]</c>: it is asked over the broker, by <c>AnswerReservationRequests</c>, through
///         the boundary's internal interface. Nobody holds stock by calling this service's API.
///     </para>
///     <para>
///         A line may be held across several locations: what matters to the order is how many, and
///         <see cref="ReservedLine.Available" /> is the sum over every location. The check comes first and
///         for all the lines together — a SKU on two lines asks for the sum of both — so a short order
///         writes nothing at all rather than half of itself.
///     </para>
///     <para>
///         ⚠️ Two orders asking at the same instant for the last units are not serialized here: each reads
///         the levels, each finds enough, and both hold. The levels carry no concurrency token yet; the
///         suite places orders one at a time.
///     </para>
///     <para>
///         <c>[Transactional]</c> because of the expiry: <c>ExpireReservationsJob</c> is scheduled for the
///         moment the holds are due, and the job store writes its row as it is scheduled. Inside the
///         transaction, the holds and the job that will give them back commit together or not at all — a
///         hold with no expiry would keep the shelf for ever, and an expiry with no hold would give back
///         stock nobody held.
///     </para>
/// </remarks>
[DomainAction]
[Transactional]
[RequirePermission(StockPermissions.Reservation.Create)]
public partial class ReserveStockAction : DomainAction<StockReservation>, ICacheInvalidator
{
    private IOptions<ReservationOptions> _options = null!;
    private IFeatureFlags _flags = null!;
    private IJobScheduler _jobs = null!;
    private IReadRepository<Product> _products = null!;
    private IRepository<StockLevel> _levels = null!;
    private IRepository<Reservation> _reservations = null!;
    private readonly ISet<Guid> _moved = new HashSet<Guid>();

    public required Guid OrderId { get; init; }

    public required List<ReservationLine> Lines { get; init; }

    [FromClock]
    public DateTimeOffset Now { get; private set; }

    public override async Task<Result<StockReservation, IError>> Execute(CancellationToken ct = default)
    {
        var skus = Lines.Select(line => line.Sku).Distinct().ToList();
        var products = await _products
            .FindAsync(Spec<Product>.Where(p => skus.Contains(p.Sku)), ct)
            .ConfigureAwait(false);

        var productIds = products.Select(p => p.PersistenceId).ToList();
        var levels = await _levels
            .FindAsync(Spec<StockLevel>.Where(l => productIds.Contains(l.ProductId)), ct)
            .ConfigureAwait(false);

        // Where each SKU can be held, most available first.
        var bySku = products.ToDictionary(
            p => p.Sku,
            p => levels.Where(l => l.ProductId == p.PersistenceId).OrderByDescending(l => l.AvailableNow()).ToList());

        var available = bySku.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(l => l.AvailableNow()));
        var asked = Lines.GroupBy(line => line.Sku).ToDictionary(g => g.Key, g => g.Sum(line => line.Quantity));
        var answer = Lines
            .Select(line =>
            {
                var there = available.GetValueOrDefault(line.Sku);
                return new ReservedLine(line.Sku, line.Quantity, there, there >= asked[line.Sku]);
            })
            .ToList();

        var reservation = new StockReservation(answer);
        var held = Lines.Select(line => line.Quantity).ToList();
        if (!reservation.IsComplete)
        {
            if (!await _flags.IsEnabledAsync<AcceptBackordersFlag>(ct).ConfigureAwait(false))
                return reservation;

            // Backorders accepted: each line holds what is left of its SKU, in order, and backorders the rest.
            var left = new Dictionary<string, int>(available);
            for (var i = 0; i < Lines.Count; i++)
            {
                held[i] = Math.Min(Lines[i].Quantity, left.GetValueOrDefault(Lines[i].Sku));
                left[Lines[i].Sku] = left.GetValueOrDefault(Lines[i].Sku) - held[i];
                answer[i] = answer[i] with { Reserved = true, Backordered = Lines[i].Quantity - held[i] };
            }

            reservation = new StockReservation(answer);
        }

        var expiresAt = Now + TimeSpan.FromSeconds(_options.Value.HoldSeconds);
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            var toHold = held[i];
            if (toHold == 0)
                continue;

            foreach (var level in bySku[line.Sku])
            {
                var here = Math.Min(toHold, level.AvailableNow());
                if (here == 0)
                    continue;

                _reservations.Add(level.Hold(OrderId, here, expiresAt));
                _moved.Add(level.ProductId);
                toHold -= here;
                if (toHold == 0)
                    break;
            }
        }

        // The order's id is the correlation: one job per order, findable by it.
        await _jobs
            .ScheduleAtAsync<ExpireReservationsJob, ExpireReservations>(
                new ExpireReservations(OrderId), expiresAt, correlationId: OrderId.ToString(), ct: ct)
            .ConfigureAwait(false);

        return reservation;
    }

    /// <summary>Drops the availability of the products this call held, and of no other.</summary>
    public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
        => AvailabilityCache.DropAsync(cache, _moved, ct);
}
