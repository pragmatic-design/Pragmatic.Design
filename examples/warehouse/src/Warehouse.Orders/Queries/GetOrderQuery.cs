namespace Warehouse.Orders.Queries;

/// <summary>One order, by id, with its lines: the route the create's <c>[CreatedAt]</c> promises.</summary>
[Query<Order, OrderDto>(Single = true)]
[RequirePermission(OrdersPermissions.Order.Read)]
[Endpoint(HttpVerb.Get, "api/orders/{id}")]
public partial class GetOrderQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
