using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;

namespace Warehouse.Stock.Infrastructure.Jobs;

/// <summary>
///     When an order's holds are due, gives back whatever nobody confirmed.
/// </summary>
/// <remarks>
///     <para>
///         One job per order, scheduled for the moment its holds expire by <c>ReserveStockAction</c>, in
///         the transaction that holds the stock. It is a row in this service's database
///         (<c>[EnableJobPersistence]</c>), not a timer in the instance that took the request: a restart,
///         or that instance stopping for good, does not lose it, and whichever instance polls first runs
///         it.
///     </para>
///     <para>
///         ⚠️ <b>Why a job per order and not a sweep</b>, which is what Casework does for its deadlines.
///         A sweep runs on a cron, so a hold would be given back up to one period late; the precision a
///         shelf needs is the hold's own time. A sweep would also have to find the due holds among all of
///         them, where a job is told which order it is for.
///     </para>
///     <para>
///         The work is the boundary's internal action, whose invoker owns the transaction. A failure is
///         thrown so the job store retries it rather than marking a hold given back that was not.
///     </para>
/// </remarks>
[Job]
internal sealed partial class ExpireReservationsJob(
    IStockReservationsInternalActions reservations,
    ILogger<ExpireReservationsJob> logger) : IJob<ExpireReservations>
{
    public async Task ExecuteAsync(ExpireReservations parameters, JobContext context, CancellationToken ct)
    {
        var expired = await reservations
            .ExpireReservations(orderId: parameters.OrderId, ct: ct)
            .ConfigureAwait(false);

        if (expired.IsFailure)
            throw new InvalidOperationException(
                $"The holds of order {parameters.OrderId} could not be given back: {expired.Error}");

        LogExpired(parameters.OrderId, expired.Value);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Order {OrderId}: {Expired} hold(s) given back (0 when the order was confirmed in time).")]
    private partial void LogExpired(Guid orderId, int expired);
}
