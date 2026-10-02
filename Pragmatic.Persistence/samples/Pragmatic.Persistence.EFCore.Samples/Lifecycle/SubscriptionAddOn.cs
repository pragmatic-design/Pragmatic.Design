using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     A default add-on created alongside a <see cref="Subscription"/> by
///     <see cref="SubscriptionPresetProvider"/>. Demonstrates the <c>[HasPresets]</c> +
///     <c>[PresetProvider]</c> child-seeding pattern.
/// </summary>
[Entity]
[Relation.ManyToOne<Subscription>]
public partial class SubscriptionAddOn : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string Name { get; set; } = "";

    public decimal Price { get; set; }
}
