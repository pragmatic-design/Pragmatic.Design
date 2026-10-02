using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Retrieves an order by ID. Demonstrates: DomainAction with return type,
///     single dependency, and NotFoundError.
/// </summary>
[DomainAction]
public partial class GetOrderAction : DomainAction<OrderRecord, NotFoundError>
{
    private IOrderRepository _orderRepository = null!;

    /// <summary>
    ///     The ID of the order to retrieve.
    /// </summary>
    public required Guid OrderId { get; init; }

    public override async Task<Result<OrderRecord, IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(OrderId, ct);

        if (order is null)
            return new NotFoundError { ResourceType = "Order", ResourceId = OrderId.ToString() };

        return order;
    }
}
