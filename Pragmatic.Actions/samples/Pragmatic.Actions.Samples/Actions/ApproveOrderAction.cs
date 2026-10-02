using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Authorization;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Approves an order. Demonstrates declarative authorization on a runnable action via
///     <c>[RequireAnyPermission]</c> — the user must hold at least one of the listed permissions.
/// </summary>
/// <remarks>
///     The <c>PermissionAuthorizationFilter</c> (Order 200) enforces this at runtime before
///     <see cref="Execute" /> runs. Internal (intra-boundary) calls bypass the check — see
///     <c>AuthorizationSample</c> for the ActionCallContext demonstration.
/// </remarks>
[DomainAction]
[RequireAnyPermission("orders.admin", "orders.manager")]
public partial class ApproveOrderAction : DomainAction<Guid, NotFoundError>
{
    private IOrderRepository _orderRepository = null!;

    /// <summary>The ID of the order to approve.</summary>
    public required Guid OrderId { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(OrderId, ct);

        if (order is null)
            return new NotFoundError { ResourceType = "Order", ResourceId = OrderId.ToString() };

        return order.Id;
    }
}
