using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Authorization;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Creates an order, gated by a single required permission via <c>[RequirePermission]</c>.
/// </summary>
/// <remarks>
///     The <c>PermissionAuthorizationFilter</c> (Order 200) requires the current user to hold
///     <c>orders.create</c>. If absent, the pipeline short-circuits with a ForbiddenError (403)
///     and <see cref="Execute" /> is never invoked.
/// </remarks>
[DomainAction]
[RequirePermission("orders.create")]
public partial class CreateOrderRestrictedAction : DomainAction<Guid, ValidationError>
{
    private IOrderRepository _orderRepository = null!;

    /// <summary>The product to order.</summary>
    public required string Product { get; init; }

    /// <summary>The quantity to order.</summary>
    public required int Quantity { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Product))
            return new ValidationError { Field = nameof(Product), Message = "Product is required." };

        var order = await _orderRepository.CreateAsync(Product, Quantity, ct);
        return order.Id;
    }
}
