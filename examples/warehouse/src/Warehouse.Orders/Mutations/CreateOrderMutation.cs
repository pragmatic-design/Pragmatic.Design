namespace Warehouse.Orders.Mutations;

/// <summary>
///     The order desk drafts an order: who it is for, and its lines.
/// </summary>
/// <remarks>
///     The number is not an input: <c>[GeneratedValue]</c> draws it from a database sequence when the
///     order is saved.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[RequirePermission(OrdersPermissions.Order.Create)]
[Endpoint(HttpVerb.Post, "api/orders")]
[CreatedAt("/api/orders/{Id}")]
[ReturnsDto<OrderDto>]
public partial class CreateOrderMutation : Mutation<Order>
{
    [Required]
    [MaxLength(64)]
    public required string CustomerReference { get; init; }

    /// <summary>What is ordered. Each line is checked by its own rules, and a draft with none is refused.</summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The lines are checked whatever the attribute says: the generated validator walks a list
    ///         whose element type is a validator, and <see cref="WriteOrderLineMutation" /> is one. What the
    ///         attribute decides is where the walk stops — at the first wrong line, answered with its index.
    ///         Written bare it would decide nothing, and that is <c>PRAG0223</c>.
    ///     </para>
    ///     <para>
    ///         The first and not all: an order arrives from a shop's system, not typed by a person, and a
    ///         wrong line there is almost always the same mistake on every line — a unit sent as zero, a
    ///         SKU field left unmapped. One answer that names the line and the rule is the report that
    ///         gets it fixed; a hundred copies of it are the same report, longer.
    ///     </para>
    /// </remarks>
    [MinCount(1)]
    [ValidateElements(StopOnFirstError = true)]
    public required List<WriteOrderLineMutation> Lines { get; init; }
}
