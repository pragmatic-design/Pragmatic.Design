using Pragmatic.Actions.Mutation;
using Pragmatic.Validation.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>
///     An order line, written <b>as a child</b>: a nested mutation, not a DTO.
/// </summary>
/// <remarks>
///     <para>
///         The rule: what nests inside a mutation is a mutation, along a declared relation. A DTO would
///         be <b>shape without behaviour</b> — no <c>ApplyAsync</c>, no validator of its own, no
///         <c>[RequirePermission]</c> — and writing a child through one means bypassing every rule the
///         child would have applied. <c>PRAG0442</c> refuses it.
///     </para>
///     <para>
///         ⚠️ <b>No mapping attribute.</b> Being a mutation gives it the mapper: <c>ApplyToEntity</c> is
///         <c>virtual</c> on <c>Mutation&lt;TEntity&gt;</c> and Actions generates the override. And no
///         projection: a mutation is write-only.
///     </para>
///     <para>
///         ⚠️ <b>No <c>[Endpoint]</c>.</b> That is what makes it a child and not an operation: without an
///         endpoint it is <c>IsInternal</c>, so <c>PRAG0438</c> does not fire even though
///         <c>OrderLine</c> is <c>[PartOf&lt;Order&gt;]</c>. Exposed, it would be the contradiction that
///         diagnostic exists to catch.
///     </para>
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteOrderLineMutation : Mutation<OrderLine>
{
    /// <summary>The key the merge pairs elements by. Without it, <c>PRAG0437</c>.</summary>
    public Guid Id { get; init; }

    public string Product { get; init; } = "";

    /// <summary>
    ///     ⚠️ The constraint that makes the child's validation measurable.
    /// </summary>
    /// <remarks>
    ///     An order line with zero quantity is an error, and if the child were not validated nobody would
    ///     say so: the write would pass, the response would be <c>200</c>. It is the case of
    ///     <c>TheChildsOwnValidation</c>.
    /// </remarks>
    [GreaterThan(0)]
    public int Quantity { get; init; }

    /// <summary>The second level, a mutation too.</summary>
    public List<WriteAllocationMutation> Allocations { get; init; } = [];
}
