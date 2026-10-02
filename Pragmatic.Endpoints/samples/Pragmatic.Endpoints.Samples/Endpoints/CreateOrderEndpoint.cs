using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Response model for order creation.
/// </summary>
public record OrderResponse(Guid OrderId, string CustomerId, decimal Total, DateTimeOffset CreatedAt);

/// <summary>
///     Creates a new order using the DomainAction pattern.
///     Demonstrates all binding scenarios for DomainAction-based endpoints:
///     - [FromRoute] for route parameters
///     - [FromHeader] for headers
///     - [FromQuery] for query parameters
///     - [FromBody] for body properties
///     - Private field for DI (uses DomainActionInvoker pattern)
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Post, "/customers/{customerId}/orders")]
[ApiSummary("Create Order")]
[ApiDescription("Creates a new order for a customer.")]
[ApiTags("Orders")]
[HttpStatus(201)]
public partial class CreateOrderEndpoint : DomainAction<OrderResponse, ValidationError>
{
    /// <summary>
    ///     The customer's unique identifier.
    /// </summary>
    [FromRoute]
    public string CustomerId { get; set; } = null!;

    /// <summary>
    ///     Idempotency key for preventing duplicate orders.
    /// </summary>
    [FromHeader(Name = "X-Idempotency-Key")]
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>
    ///     Whether to apply discount.
    /// </summary>
    [FromQuery]
    public bool ApplyDiscount { get; set; }

    /// <summary>
    ///     The order total amount.
    /// </summary>
    [FromBody]
    public decimal TotalAmount { get; set; }

    /// <summary>
    ///     Optional notes for the order.
    /// </summary>
    [FromBody]
    public string? Notes { get; set; }

    /// <inheritdoc />
    public override Task<Result<OrderResponse, IError>> Execute(CancellationToken ct = default)
    {
        // Validate
        if (TotalAmount <= 0)
            return Task.FromResult<Result<OrderResponse, IError>>(new ValidationError
            {
                Field = nameof(TotalAmount),
                Message = "Total amount must be greater than zero."
            });

        // Calculate total with discount
        var finalTotal = ApplyDiscount ? TotalAmount * 0.9m : TotalAmount;

        var response = new OrderResponse(
            Guid.NewGuid(),
            CustomerId,
            finalTotal,
            DateTimeOffset.UtcNow);

        return Task.FromResult<Result<OrderResponse, IError>>(response);
    }
}