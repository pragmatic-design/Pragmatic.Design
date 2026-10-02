using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Domain.Errors;
using Pragmatic.Integration.Tests.Domain.Models;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Gets an order by ID. Demonstrates GET endpoint with route parameter
///     and typed NotFoundError.
/// </summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/orders/{id}")]
public partial class GetOrderAction : DomainAction<OrderDetailResponse>
{
    /// <summary>
    ///     The order ID to retrieve.
    /// </summary>
    [FromRoute]
    public Guid Id { get; set; }

    // Dependency: injected via the generated SetDependencies/Invoker
    private TestDbContext _dbContext = null!;

    /// <inheritdoc />
    public override async Task<Result<OrderDetailResponse, IError>> Execute(CancellationToken ct = default)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == Id, ct).ConfigureAwait(false);

        if (order is null)
        {
            return new NotFoundError
            {
                ResourceType = "Order",
                ResourceId = Id.ToString()
            };
        }

        return new OrderDetailResponse(
            order.Id,
            order.Name,
            order.Amount,
            order.Status,
            order.CreatedAt);
    }
}
