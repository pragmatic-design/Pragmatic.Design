using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Cascade;

/// <summary>
///     Source entity for a cascade. When its <see cref="Rate"/> changes, the SG-generated
///     cascade handler propagates the new value to <see cref="BookingCharge.UnitPrice"/>
///     for every related charge.
/// </summary>
[Entity]
public partial class RoomRate : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string RoomClass { get; set; } = "";

    public decimal Rate { get; set; }
}
