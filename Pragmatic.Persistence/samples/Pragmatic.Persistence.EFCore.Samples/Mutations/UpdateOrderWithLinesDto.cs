using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO demonstrating collection patches.
///     The Lines collection contains nested patch DTOs that will be applied
///     to the order's Lines collection.
/// </summary>
[Patch<Order>]
public partial class UpdateOrderWithLinesDto
{
    /// <summary>
    ///     Updated order status.
    /// </summary>
    public OrderStatus? Status { get; init; }

    /// <summary>
    ///     Updated notes.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    ///     Collection of line patches, matched against target.Lines by Id.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Updates only. The element is a <c>[Patch&lt;OrderLine&gt;]</c>, which carries a delta and
    ///     has no way to build a whole entity, so an element matching nothing is skipped rather than
    ///     created — PRAG2205 says so at compile time. Give the element <c>[MapTo&lt;OrderLine&gt;]</c>
    ///     to add as well as update.
    /// </remarks>
    public List<UpdateOrderLineDto>? Lines { get; init; }
}
