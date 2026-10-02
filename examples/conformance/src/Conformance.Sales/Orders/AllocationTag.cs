using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The third level: <c>Order → Lines → Allocations → Tags</c>.
/// </summary>
/// <remarks>
///     <para>
///         It exists for one reason: depth 3 must be <b>executed</b>, not only snapshotted. A snapshot
///         compares the generated text; it does not send it to a database.
///     </para>
///     <para>
///         ⚠️ It is the level that tests the <b>two-hop</b> prefix. For a change here to keep identity,
///         the invoker must include <c>Lines.Allocations.Tags</c>: it takes <c>Allocations.Tags</c> from
///         <c>OrderLineDto.WrittenNavigations</c> — which in turn got it by prefixing <c>Tags</c> from
///         <c>AllocationDto</c> — and puts <c>Lines</c> in front. Every wrong link produces the same
///         silent rewrite as depth 2.
///     </para>
/// </remarks>
[Entity]
[PartOf<Allocation>]
[Relation.OneToMany<TagNote>.WithNavigation("Notes")]
[Relation.ManyToOne<Allocation>]
public partial class AllocationTag : IEntity
{
    [Required]
    public string Label { get; private set; } = "";

    /// <summary>
    ///     ⚠️ A <c>[DefaultValue]</c> on a <b>string</b>: the shape that would stop startup.
    /// </summary>
    /// <remarks>
    ///     Raw in the DDL, the C# literal <c>"pending"</c> in double quotes is an identifier in
    ///     PostgreSQL: <c>0A000: cannot use column reference in DEFAULT expression</c>, migration
    ///     refused. A numeric <c>[DefaultValue]</c> hides it, because there the two literals happen to
    ///     coincide — this one is a string, and it is here so that it stays one.
    /// </remarks>
    [DefaultValue("pending")]
    public string Status { get; private set; } = "";
}
