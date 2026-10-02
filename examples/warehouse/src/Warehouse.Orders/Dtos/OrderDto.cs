namespace Warehouse.Orders.Dtos;

/// <summary>An order with its lines, as it is read back.</summary>
[MapFrom<Order>]
[GenerateProjection]
public partial class OrderDto
{
    public Guid Id { get; init; }

    public string Number { get; init; } = "";

    public string CustomerReference { get; init; } = "";

    public OrderStatus Status { get; init; }

    public string? CancellationReason { get; init; }

    public List<OrderLineDto> Lines { get; init; } = [];
}
