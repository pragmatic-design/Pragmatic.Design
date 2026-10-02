using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     Seeds default child entities when a <see cref="Subscription"/> is created.
///     Implements <see cref="IPresetProvider{TParent}"/> — declared on the parent via
///     <c>[HasPresets]</c> + <c>[PresetProvider&lt;SubscriptionPresetProvider&gt;]</c>.
///     The host adds the returned entities in the same unit of work as the parent.
/// </summary>
public sealed class SubscriptionPresetProvider : IPresetProvider<Subscription>
{
    public Task<IReadOnlyList<object>> CreatePresetsAsync(
        Subscription parent, LifecycleContext context, CancellationToken ct)
    {
        // Every new subscription starts with a free "Email Support" add-on,
        // and paid plans also get "Priority Support".
        var emailSupport = new SubscriptionAddOn { Name = "Email Support", Price = 0m };
        emailSupport.SetSubscriptionId(parent.Id);
        var addOns = new List<object> { emailSupport };

        if (parent.MonthlyPrice >= 49m)
        {
            var prioritySupport = new SubscriptionAddOn { Name = "Priority Support", Price = 19m };
            prioritySupport.SetSubscriptionId(parent.Id);
            addOns.Add(prioritySupport);
        }

        return Task.FromResult<IReadOnlyList<object>>(addOns);
    }
}
