namespace Warehouse.Orders.Infrastructure.Authorization;

/// <summary>
///     Takes the orders: drafts them, sends them, reads them, and cancels one that has not left.
/// </summary>
[Role("order-desk", "Drafts, places, reads and cancels orders")]
[Grants(
    OrdersPermissions.Order.Create,
    OrdersPermissions.Order.Read,
    OrdersPermissions.Order.Place,
    OrdersPermissions.Order.Confirm,
    OrdersPermissions.Order.Cancel)]
public sealed partial class OrderDeskRole;
