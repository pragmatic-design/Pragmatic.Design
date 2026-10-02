using Pragmatic.Actions.Mutation;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>The second level, as a mutation child.</summary>
/// <remarks>
///     It also carries the value object, which stays a domain type passed by value: a
///     <c>[ValueObject]</c> is not a child, it is a value, and it does not need to be a mutation.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteAllocationMutation : Mutation<Allocation>
{
    public Guid Id { get; init; }

    public string Warehouse { get; init; } = "";

    public int Quantity { get; init; }

    public StorageSlot Slot { get; init; } = new("", 0);

    /// <summary>The third level.</summary>
    public List<WriteAllocationTagMutation> Tags { get; init; } = [];
}
