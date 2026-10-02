using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Errors;
using Pragmatic.Actions.Samples.Services;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Creates a new order. Demonstrates: DomainAction with return type,
///     multiple dependencies, and typed error.
/// </summary>
/// <remarks>
///     The source generator will produce:
///     - PlaceOrderAction.SetDependencies.g.cs (partial with SetDependencies method)
///     - PlaceOrderAction.Invoker.g.cs (sealed PlaceOrderActionInvoker + DI extension)
/// </remarks>
[DomainAction]
public partial class PlaceOrderAction : DomainAction<Guid, ValidationError>
{
    private IOrderRepository _orderRepository = null!;
    private IEmailService _emailService = null!;

    /// <summary>
    ///     The product to order.
    /// </summary>
    public required string Product { get; init; }

    /// <summary>
    ///     The quantity to order.
    /// </summary>
    public required int Quantity { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Product))
            return new ValidationError { Field = nameof(Product), Message = "Product is required." };

        if (Quantity <= 0)
            return new ValidationError { Field = nameof(Quantity), Message = "Quantity must be positive." };

        var order = await _orderRepository.CreateAsync(Product, Quantity, ct);
        await _emailService.SendAsync("admin@example.com", "New Order", $"Order {order.Id} placed.", ct);

        return order.Id;
    }
}
