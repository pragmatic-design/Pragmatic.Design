using Conformance.Catalog.Entities;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The aggregate of the <b>1:N depth 1</b> case.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: a collection of DTOs is <b>merged by key</b>, not assigned, so the existing
///         rows keep their identity.
///     </para>
///     <para>
///         ⚠️ The <c>Lines</c> navigation is <b>not declared here</b>: Persistence generates it from
///         <c>[Relation.OneToMany]</c>. The name is given with <c>WithNavigation</c>, because the default
///         is the related type in the plural — <c>OrderLines</c> — and a property named otherwise is
///         <c>PRAG0439</c>. It is deliberate — it means that when Mapping and Actions run, <c>Lines</c>
///         does not exist as a symbol, and they must ask <c>TraitPropertyResolver</c> for it. The case
///         exercises that path instead of bypassing it by declaring the property by hand.
///     </para>
/// </remarks>
/// <remarks>
///     <para>
///         ⚠️ <c>[Relation.ManyToOne&lt;CatalogItem&gt;]</c> crosses the <b>boundary</b>:
///         <c>CatalogItem</c> belongs to <c>CatalogBoundary</c>. It is declared on purpose, to pin what
///         happens with nesting across the boundary.
///     </para>
///     <para>
///         The generator always emits the <b>foreign key</b> <c>ItemId</c>, and the <c>Item</c>
///         navigation because <c>SalesBoundary</c> declares <c>[ReadAccess&lt;CatalogItem&gt;]</c>:
///         without that declaration only the key would remain. The navigation is read-only — a mutation
///         that tried to nest a child from over there gets <c>PRAG0444</c>, and the context refuses to
///         commit what it holds read-only (<c>TheBoundaryAsAWall</c>,
///         <c>ReadAccessAcrossTheBoundary</c>).
///     </para>
/// </remarks>
/// <remarks>
///     <para>
///         <c>[GenerateGridBridge]</c> is the bridge <c>SearchOrdersGridQuery</c> goes through: a
///         canonical grid request is applied from there, and the bridge is an <b>allow-list</b> — only
///         what carries <c>[Filterable]</c> can be named from the wire.
///     </para>
///     <para>
///         ⚠️ <c>Notes</c> exists and does not carry it, on purpose: it is the control half of the case,
///         without which «the filter applies» would also be satisfied by a bridge that accepts any
///         column name.
///     </para>
/// </remarks>
[Entity]
[GenerateGridBridge]
[Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
[Relation.ManyToOne<CatalogItem>.WithNavigation("Item")]
[Relation.ManyToOne<DeliveryAddress>.WithNavigation("DeliveryAddress", Required = false)]
[Relation.ManyToMany<Label>.WithNavigation("Labels")]
public partial class Order : IEntity
{
    /// <summary>A scalar field, to show that the write reaches the root too.</summary>
    [Required]
    [Filterable]
    public string Reference { get; private set; } = "";

    /// <summary>
    ///     A field that can be <b>absent</b>: the target of an explicit null.
    /// </summary>
    /// <remarks>
    ///     Used by <c>TheExplicitNull</c>: a <c>[Patch&lt;T&gt;]</c> tells «clear it» from «I am not
    ///     talking about it» only on a property that admits null, and <c>Reference</c> is
    ///     <c>[Required]</c>.
    /// </remarks>
    public string? Notes { get; private set; }

    /// <summary>
    ///     How many lines the order has, maintained by the framework.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ It is the only <c>Count</c> <c>[RollUp]</c> outside the inventory corpus: counting the
    ///         lines exercises the <c>Count</c> member of the enum, where a <c>Sum</c> on a
    ///         <c>decimal</c> would not.
    ///     </para>
    ///     <para>
    ///         The child property is the empty string because a count reads none, and that is what the
    ///         attribute documents. Declaring one would send it into the rule as an amount.
    ///     </para>
    /// </remarks>
    [RollUp<OrderLine>("", RollUpAggregation.Count)]
    public int LineCount { get; private set; }
}
