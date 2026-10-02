using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Archives an order. Demonstrates: VoidDomainAction with typed error
///     and dependency.
/// </summary>
[DomainAction]
public partial class ArchiveOrderAction : VoidDomainAction<NotFoundError>
{
    private IOrderRepository _orderRepository = null!;

    /// <summary>
    ///     The ID of the order to archive.
    /// </summary>
    public required Guid OrderId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(OrderId, ct);

        if (order is null)
            return new NotFoundError { ResourceType = "Order", ResourceId = OrderId.ToString() };

        // Archive logic would go here
        return Success;
    }
}
