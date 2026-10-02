using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Cascade;

/// <summary>
///     Target entity for a cascade. <c>[CascadeOn&lt;RoomRate&gt;(nameof(RoomRate.Rate))]</c> on
///     <see cref="UnitPrice"/> makes the SG emit <c>BookingChargeUnitPriceCascadeHandler</c>,
///     an <c>IDomainEventHandler&lt;EntityPropertyChanged&lt;RoomRate&gt;&gt;</c> that runs an
///     <c>ExecuteUpdateAsync</c> setting <c>UnitPrice</c> for every charge whose
///     <c>RoomRateId</c> matches the changed rate.
/// </summary>
[Entity]
[Relation.ManyToOne<RoomRate>]
public partial class BookingCharge : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string Label { get; set; } = "";

    // In a real project this property carries [CascadeOn<RoomRate>(nameof(RoomRate.Rate))], and the
    // SG emits BookingChargeUnitPriceCascadeHandler (an IDomainEventHandler<EntityPropertyChanged<RoomRate>>).
    // The attribute is omitted here so the sample stays self-contained; the sample wires the exact
    // equivalent handler by hand (see CascadeSample) to show the runtime behaviour.

    /// <summary>Cascaded from <see cref="RoomRate.Rate"/> when that rate changes.</summary>
    public decimal UnitPrice { get; set; }
}
