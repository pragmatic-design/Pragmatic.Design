using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Warehouse.Stock.Contracts;

namespace Warehouse.Stock.Infrastructure.RequestHandlers;

/// <summary>
///     Orders asks for an order's stock and waits: this answers, from whichever Stock instance took the
///     request off the queue.
/// </summary>
/// <remarks>
///     <para>
///         The one request/reply in the examples, and on purpose: placing an order cannot finish until
///         the stock is known to be held, so the answer belongs inside the customer's request. Everywhere
///         else a service tells another what happened and does not wait (Casework).
///     </para>
///     <para>
///         The queue is named after the request type — <c>requests.reserve-stock</c> — and not after
///         this module, so both instances consume the same queue and each request is answered once.
///     </para>
///     <para>
///         Through the internal interface: a request arrives with no principal, and the internal surface
///         is the one that runs as an internal call. The action's invoker owns the transaction.
///     </para>
///     <para>
///         ⚠️ <b>Holds meet other writers.</b> A hold raises its level's <c>Reserved</c>, and so does every
///         other hold on the same product; the expiry of an earlier order lowers it, on either instance.
///         The level is <c>[ConcurrencyAware]</c>, so the second writer's save fails instead of undoing the
///         first one's. Here that is not an answer for the customer, whose order would be refused for a
///         conflict that says nothing about the stock: the reservation is made again, from the start, in a
///         fresh scope, and reads what the other writer committed. The same pattern as an import's parts
///         (<c>ApplyImportParts</c>).
///     </para>
/// </remarks>
[RequestHandler]
internal sealed partial class AnswerReservationRequests(
    IServiceScopeFactory scopes,
    ILogger<AnswerReservationRequests> logger) : IRequestHandler<ReserveStock, StockReservation>
{
    /// <summary>How many times a reservation is made before its conflicts are given up on.</summary>
    private const int Attempts = 5;

    public async Task<StockReservation> HandleAsync(
        ReserveStock request, MessageContext context, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ReserveAsync(request, ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < Attempts)
            {
                LogLevelMet(request.OrderId, attempt);
            }
        }
    }

    /// <summary>One attempt, in a scope of its own: a failed save leaves its DbContext unusable.</summary>
    private async Task<StockReservation> ReserveAsync(ReserveStock request, CancellationToken ct)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var reservations = scope.ServiceProvider.GetRequiredService<IStockReservationsInternalActions>();
            var held = await reservations
                .ReserveStock(orderId: request.OrderId, lines: [.. request.Lines], ct: ct)
                .ConfigureAwait(false);

            // A refusal that is not "short" — nothing here expects one — goes back to Orders as the
            // responder's error, which the requester reads as a service that could not answer.
            return held.IsSuccess
                ? held.Value
                : throw new InvalidOperationException(
                    $"The stock for order {request.OrderId} could not be reserved: {held.Error}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The hold for order {OrderId} met another writer of its level (attempt {Attempt}): reserving again.")]
    private partial void LogLevelMet(Guid orderId, int attempt);
}
