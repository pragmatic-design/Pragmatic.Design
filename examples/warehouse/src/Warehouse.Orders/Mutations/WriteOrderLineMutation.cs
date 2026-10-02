namespace Warehouse.Orders.Mutations;

/// <summary>
///     A line, written as a child of its order: a mutation, not a DTO.
/// </summary>
/// <remarks>
///     No <c>[Endpoint]</c>, and that is what makes it a child rather than an operation. Its rules travel
///     with it, so every line of an order is checked by the same two rules, one line at a time.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteOrderLineMutation : Mutation<OrderLine>
{
    /// <summary>The key the merge pairs on: an id that is already there updates the line, a new one adds it.</summary>
    public Guid Id { get; init; }

    [Required]
    [MaxLength(40)]
    public string Sku { get; init; } = "";

    [GreaterThan(0)]
    public int Quantity { get; init; }
}
