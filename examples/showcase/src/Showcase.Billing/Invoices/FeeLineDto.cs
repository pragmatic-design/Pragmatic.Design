namespace Showcase.Billing.Dtos;

/// <summary>
/// A fee with the invoice it belongs to, resolved in one query.
/// </summary>
/// <remarks>
/// This exists to keep a generated navigation on the read path. <c>Fee</c> declares
/// <c>[Relation.ManyToOne&lt;Invoice&gt;]</c>, so <c>Invoice</c> is written by the generator and does
/// not exist while Mapping runs. A projection that flattens through it must still resolve it, and a
/// failure there drops the property with no diagnostic at all — which nothing notices unless a DTO
/// actually crosses a generated navigation.
/// </remarks>
/// <remarks>
/// <c>Fee</c> is a TPH hierarchy, and mapping every row to this one shape answers "45 EUR" to a guest
/// asking <em>what for</em> — so the two derived shapes are declared here and
/// <c>FromEntity(fee)</c> returns whichever the row actually is.
/// <para>
/// ⚠️ <b>Runtime only.</b> <c>PRAG0331</c> says it on the build: the EF projection keeps the base
/// shape, because a projection is one expression and the discriminator is not known until the row is
/// read. A read that needs the derived shape materialises the entity and maps it — which is what the
/// case asserting this does.
/// </para>
/// <para>
/// ⚠️ A <c>[MapFrom]</c> DTO inheriting another hides the base's generated statics, and the source walk
/// sees a member the base entity declares — both pinned by <c>ADtoThatInheritsAMappedDtoTests</c>.
/// </para>
/// </remarks>
[MapFrom<Fee>]
[GenerateProjection]
[MapDerived<ServiceFee, ServiceFeeLineDto>]
[MapDerived<CancellationFee, CancellationFeeLineDto>]
public partial class FeeLineDto
{
    public Guid Id { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "";

    /// <summary>Flattened through the generated navigation, not through a declared property.</summary>
    [MapProperty("Invoice.InvoiceNumber")]
    public string InvoiceNumber { get; init; } = "";

    /// <summary>The foreign key the same relation generates.</summary>
    public Guid InvoiceId { get; init; }
}
