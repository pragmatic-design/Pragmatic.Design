using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Integration.Tests.Domain.Models;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Result;
using Pragmatic.Validation.Attributes;

namespace Pragmatic.Integration.Tests.Domain.Actions;

/// <summary>
///     Creates a new order via query parameters.
///     Validates Name (required, not empty) and Amount (positive) through
///     generated ISyncValidator, then persists to InMemory EF Core.
/// </summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Post, "/api/orders")]
public partial class CreateOrderAction : DomainAction<OrderResponse>
{
    /// <summary>
    ///     The order name. Must not be empty.
    /// </summary>
    [FromQuery]
    [Required]
    [NotEmpty]
    public string Name { get; set; } = null!;

    /// <summary>
    ///     The order amount. Must be positive.
    /// </summary>
    [FromQuery]
    [Positive]
    public decimal Amount { get; set; }

    // Dependency: injected via the generated SetDependencies/Invoker
    private TestDbContext _dbContext = null!;

    /// <inheritdoc />
    public override async Task<Result<OrderResponse, IError>> Execute(CancellationToken ct = default)
    {
        var order = new TestOrder
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Amount = Amount,
            Status = "Created",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        return new OrderResponse(order.Id, order.Name, order.Amount, order.Status);
    }
}
