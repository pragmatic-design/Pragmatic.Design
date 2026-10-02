using Pragmatic.Persistence.Patch;
using Conformance.Sales.Dtos;

namespace Conformance.Sales.Entities;

/// <summary>
///     A patch that carries children — the shape that publishes <c>WrittenNavigations</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is the only write door for which <b>nobody loads</b>: `ApplyPatch` is invoked by
///         hand-written code, not by a generated invoker. And a patch merges — it decides what to remove
///         by looking at what is on the entity — so against a collection nobody loaded it removes nothing
///         and <b>adds everything</b>.
///     </para>
///     <para>
///         That is why the type publishes the list of what it writes, deep and prefixed, and whoever
///         uses it passes it to <c>INavigationLoader.EnsureLoadedAsync</c>. The two together are measured
///         by <c>ThePatchThatCarriesChildren</c>: without the load the lines double.
///     </para>
///     <para>
///         ⚠️ Absent and null are two different values, and the generated JSON converter makes the
///         difference: it calls <c>MarkSet</c> for every property the body names, and
///         <c>ApplyPatch</c> applies those. Without that producer every patch would fall back to «write
///         what is not null», where <c>{"notes": null}</c> and a body without <c>notes</c> would be the
///         same thing. <c>TheExplicitNull</c> measures it.
///     </para>
/// </remarks>
[Patch<Order>]
public partial class UpdateOrderPatch
{
    public string? Reference { get; init; }

    /// <summary>A null here <b>clears</b>; not naming it leaves it as it is.</summary>
    public string? Notes { get; init; }

    /// <summary>The lines, by key: `WrittenNavigations` will contain `Lines`.</summary>
    public List<OrderLineDto> Lines { get; init; } = [];
}
